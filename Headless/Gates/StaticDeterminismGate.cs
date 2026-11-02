using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using NUnit.Framework;

namespace VolleyDuel.Gates
{
    /// <summary>
    /// Static gate: the deterministic assemblies must not touch floating point, wall clocks,
    /// System.Random, threads, hashed collections or UnityEngine. The scan reads compiled
    /// metadata and IL, so it sees what actually ships, including implicit conversions.
    /// </summary>
    public class StaticDeterminismGate
    {
        /// <summary>Assemblies under the gate. Sim per the contract; AI too, so the Balance Lab is reproducible.</summary>
        public static readonly string[] GatedAssemblies =
        {
            "Duel.Core.Sim",
            "VolleyDuel.Sim",
            "Duel.Core.AI",
            "VolleyDuel.AI",
        };

        static readonly string[] BannedTypePrefixes =
        {
            "UnityEngine.",
            "UnityEditor.",
            "System.Threading.",
            "System.Diagnostics.Stopwatch",
        };

        static readonly HashSet<string> BannedTypes = new HashSet<string>
        {
            "System.Single",
            "System.Double",
            "System.Decimal",
            "System.Half",
            "System.MathF",
            "System.Random",
            "System.DateTime",
            "System.DateTimeOffset",
            "System.TimeSpan",
            "System.Environment",
            "System.Guid",
            "System.HashCode",
            "System.Collections.Hashtable",
            "System.Collections.Generic.Dictionary`2",
            "System.Collections.Generic.HashSet`1",
            "System.Collections.Generic.SortedDictionary`2",
            "System.Collections.Concurrent.ConcurrentDictionary`2",
            "System.Runtime.InteropServices.Marshal",
        };

        static readonly HashSet<ushort> FloatOpcodes = new HashSet<ushort>(new[]
        {
            OpCodes.Ldc_R4, OpCodes.Ldc_R8, OpCodes.Conv_R4, OpCodes.Conv_R8, OpCodes.Conv_R_Un,
            OpCodes.Ldind_R4, OpCodes.Ldind_R8, OpCodes.Stind_R4, OpCodes.Stind_R8, OpCodes.Ldelem_R4,
            OpCodes.Ldelem_R8, OpCodes.Stelem_R4, OpCodes.Stelem_R8, OpCodes.Ckfinite,
        }.Select(o => (ushort)o.Value));

        static readonly Dictionary<ushort, OpCode> OpcodeTable = typeof(OpCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => (OpCode)f.GetValue(null))
            .ToDictionary(o => (ushort)o.Value, o => o);

        public static IEnumerable<string> Assemblies() => GatedAssemblies;

        [TestCaseSource(nameof(Assemblies))]
        public void AssemblyIsDeterministic(string assemblyName)
        {
            string path = Path.Combine(TestContext.CurrentContext.TestDirectory, assemblyName + ".dll");
            Assert.IsTrue(File.Exists(path), "Gated assembly not found next to the gate tests: " + path + ". Reference its project from VolleyDuel.Gates.csproj.");
            List<string> violations = Scan(path);
            if (violations.Count > 0)
            {
                Assert.Fail(assemblyName + " breaks the determinism contract:\n  " + string.Join("\n  ", violations.Distinct().Take(60)));
            }
        }

        [Test]
        public void GateCatchesEveryBannedCategory()
        {
            // Self-test against GateCanary, which breaks each rule on purpose.
            List<string> violations = Scan(typeof(StaticDeterminismGate).Assembly.Location).Where(v => v.Contains("GateCanary")).ToList();
            string all = string.Join("\n", violations);
            StringAssert.Contains("float opcode", all);
            StringAssert.Contains("System.Single", all);
            StringAssert.Contains("System.Double", all);
            Assert.That(Scan(typeof(StaticDeterminismGate).Assembly.Location).Any(v => v.Contains("System.DateTime")), Is.True, "DateTime");
            Assert.That(Scan(typeof(StaticDeterminismGate).Assembly.Location).Any(v => v.Contains("System.Random")), Is.True, "System.Random");
            Assert.That(Scan(typeof(StaticDeterminismGate).Assembly.Location).Any(v => v.Contains("Dictionary`2")), Is.True, "Dictionary");
        }

        public static List<string> Scan(string path)
        {
            var violations = new List<string>();
            using (var stream = File.OpenRead(path))
            using (var pe = new PEReader(stream))
            {
                MetadataReader md = pe.GetMetadataReader();

                foreach (AssemblyReferenceHandle h in md.AssemblyReferences)
                {
                    string name = md.GetString(md.GetAssemblyReference(h).Name);
                    if (name.StartsWith("UnityEngine", StringComparison.Ordinal) || name.StartsWith("UnityEditor", StringComparison.Ordinal))
                    {
                        violations.Add("references assembly " + name);
                    }
                }

                foreach (TypeReferenceHandle h in md.TypeReferences)
                {
                    string full = FullName(md, md.GetTypeReference(h));
                    if (IsBanned(full)) violations.Add("references type " + full);
                }

                var provider = new TypeNameProvider(md);
                foreach (MemberReferenceHandle h in md.MemberReferences)
                {
                    MemberReference m = md.GetMemberReference(h);
                    string owner = OwnerName(md, m.Parent);
                    string member = md.GetString(m.Name);
                    foreach (string t in DecodeMember(md, m, provider))
                    {
                        if (IsBanned(t)) violations.Add("calls " + owner + "::" + member + " using " + t);
                    }
                }

                foreach (TypeDefinitionHandle th in md.TypeDefinitions)
                {
                    TypeDefinition type = md.GetTypeDefinition(th);
                    string typeName = md.GetString(type.Namespace) + "." + md.GetString(type.Name);
                    foreach (FieldDefinitionHandle fh in type.GetFields())
                    {
                        FieldDefinition f = md.GetFieldDefinition(fh);
                        string t = f.DecodeSignature(provider, null);
                        if (IsBanned(t)) violations.Add("field " + typeName + "." + md.GetString(f.Name) + " is " + t);
                    }
                    foreach (MethodDefinitionHandle mh in type.GetMethods())
                    {
                        MethodDefinition method = md.GetMethodDefinition(mh);
                        string methodName = typeName + "." + md.GetString(method.Name);
                        MethodSignature<string> sig = method.DecodeSignature(provider, null);
                        foreach (string t in sig.ParameterTypes.Concat(new[] { sig.ReturnType }))
                        {
                            if (IsBanned(t)) violations.Add("method " + methodName + " signature uses " + t);
                        }
                        if (method.RelativeVirtualAddress == 0) continue;
                        MethodBodyBlock body = pe.GetMethodBody(method.RelativeVirtualAddress);
                        if (!body.LocalSignature.IsNil)
                        {
                            StandaloneSignature locals = md.GetStandaloneSignature(body.LocalSignature);
                            foreach (string t in locals.DecodeLocalSignature(provider, null))
                            {
                                if (IsBanned(t)) violations.Add("method " + methodName + " has a local of type " + t);
                            }
                        }
                        foreach (ushort op in Opcodes(body.GetILBytes()))
                        {
                            if (FloatOpcodes.Contains(op)) violations.Add("method " + methodName + " uses float opcode " + OpcodeTable[op].Name);
                        }
                    }
                }
            }
            return violations;
        }

        static bool IsBanned(string fullName)
        {
            if (string.IsNullOrEmpty(fullName)) return false;
            foreach (string part in fullName.Split(new[] { '<', '>', ',', '[', ']', '*', '&', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (BannedTypes.Contains(part)) return true;
                foreach (string prefix in BannedTypePrefixes)
                {
                    if (part.StartsWith(prefix, StringComparison.Ordinal)) return true;
                }
            }
            return false;
        }

        static IEnumerable<string> DecodeMember(MetadataReader md, MemberReference m, TypeNameProvider provider)
        {
            if (m.GetKind() == MemberReferenceKind.Field)
            {
                yield return m.DecodeFieldSignature(provider, null);
                yield break;
            }
            MethodSignature<string> sig = m.DecodeMethodSignature(provider, null);
            yield return sig.ReturnType;
            foreach (string p in sig.ParameterTypes) yield return p;
            yield return OwnerName(md, m.Parent);
        }

        static string OwnerName(MetadataReader md, EntityHandle parent)
        {
            switch (parent.Kind)
            {
                case HandleKind.TypeReference: return FullName(md, md.GetTypeReference((TypeReferenceHandle)parent));
                case HandleKind.TypeDefinition:
                    TypeDefinition d = md.GetTypeDefinition((TypeDefinitionHandle)parent);
                    return md.GetString(d.Namespace) + "." + md.GetString(d.Name);
                case HandleKind.TypeSpecification:
                    return md.GetTypeSpecification((TypeSpecificationHandle)parent).DecodeSignature(new TypeNameProvider(md), null);
                default: return parent.Kind.ToString();
            }
        }

        static string FullName(MetadataReader md, TypeReference t)
        {
            string ns = md.GetString(t.Namespace);
            string name = md.GetString(t.Name);
            if (t.ResolutionScope.Kind == HandleKind.TypeReference)
            {
                return FullName(md, md.GetTypeReference((TypeReferenceHandle)t.ResolutionScope)) + "+" + name;
            }
            return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
        }

        static IEnumerable<ushort> Opcodes(byte[] il)
        {
            int i = 0;
            while (i < il.Length)
            {
                ushort value = il[i];
                int size = 1;
                if (value == 0xFE)
                {
                    value = (ushort)(0xFE00 | il[i + 1]);
                    size = 2;
                }
                if (!OpcodeTable.TryGetValue(value, out OpCode op)) throw new InvalidDataException("Unknown opcode 0x" + value.ToString("X"));
                yield return value;
                i += size;
                switch (op.OperandType)
                {
                    case OperandType.InlineNone: break;
                    case OperandType.ShortInlineBrTarget:
                    case OperandType.ShortInlineI:
                    case OperandType.ShortInlineVar: i += 1; break;
                    case OperandType.InlineVar: i += 2; break;
                    case OperandType.InlineI8:
                    case OperandType.InlineR: i += 8; break;
                    case OperandType.InlineSwitch:
                        int n = BitConverter.ToInt32(il, i);
                        i += 4 + n * 4;
                        break;
                    default: i += 4; break;
                }
            }
        }

        sealed class TypeNameProvider : ISignatureTypeProvider<string, object>
        {
            readonly MetadataReader md;

            public TypeNameProvider(MetadataReader md)
            {
                this.md = md;
            }

            public string GetPrimitiveType(PrimitiveTypeCode typeCode)
            {
                switch (typeCode)
                {
                    case PrimitiveTypeCode.Single: return "System.Single";
                    case PrimitiveTypeCode.Double: return "System.Double";
                    default: return "System." + typeCode;
                }
            }

            public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
            {
                TypeDefinition d = reader.GetTypeDefinition(handle);
                return reader.GetString(d.Namespace) + "." + reader.GetString(d.Name);
            }

            public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => FullName(reader, reader.GetTypeReference(handle));
            public string GetTypeFromSpecification(MetadataReader reader, object genericContext, TypeSpecificationHandle handle, byte rawTypeKind) => reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
            public string GetSZArrayType(string elementType) => elementType + "[]";
            public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[,]";
            public string GetByReferenceType(string elementType) => elementType + "&";
            public string GetPointerType(string elementType) => elementType + "*";
            public string GetPinnedType(string elementType) => elementType;
            public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) => genericType + "<" + string.Join(",", typeArguments) + ">";
            public string GetGenericMethodParameter(object genericContext, int index) => "!!" + index;
            public string GetGenericTypeParameter(object genericContext, int index) => "!" + index;
            public string GetFunctionPointerType(MethodSignature<string> signature) => "fnptr";
            public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
        }
    }
}
