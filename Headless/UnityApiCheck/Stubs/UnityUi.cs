#pragma warning disable CS0067, CS1591, CA1822, CA2211, CA1805, IDE0060
using UnityEngine;
using UnityEngine.Events;

namespace UnityEngine.EventSystems
{
    public abstract class UIBehaviour : MonoBehaviour { }
    public class EventSystem : UIBehaviour { }
    public abstract class BaseInputModule : UIBehaviour { }

    public class PointerEventData
    {
        public Vector2 position { get; set; }
        public int pointerId { get; set; }
        public Camera pressEventCamera => null;
    }

    public interface IEventSystemHandler { }
    public interface IPointerDownHandler : IEventSystemHandler { void OnPointerDown(PointerEventData eventData); }
    public interface IPointerUpHandler : IEventSystemHandler { void OnPointerUp(PointerEventData eventData); }
    public interface IDragHandler : IEventSystemHandler { void OnDrag(PointerEventData eventData); }
}

namespace UnityEngine.UI
{
    using UnityEngine.EventSystems;

    public abstract class Graphic : UIBehaviour
    {
        public virtual Color color { get; set; }
        public bool raycastTarget { get; set; }
        public RectTransform rectTransform => null;
    }

    public abstract class MaskableGraphic : Graphic { }

    public class Image : MaskableGraphic { }

    public struct ColorBlock
    {
        public Color normalColor { get; set; }
        public Color highlightedColor { get; set; }
        public Color pressedColor { get; set; }
    }

    public class Selectable : UIBehaviour
    {
        public Graphic targetGraphic { get; set; }
        public ColorBlock colors { get; set; }
        public bool interactable { get; set; }
    }

    public class Button : Selectable
    {
        public class ButtonClickedEvent : UnityEvent { }
        public ButtonClickedEvent onClick { get; } = new ButtonClickedEvent();
    }

    public class CanvasScaler : UIBehaviour
    {
        public enum ScaleMode { ConstantPixelSize, ScaleWithScreenSize, ConstantPhysicalSize }
        public ScaleMode uiScaleMode { get; set; }
        public Vector2 referenceResolution { get; set; }
        public float matchWidthOrHeight { get; set; }
    }

    public class GraphicRaycaster : MonoBehaviour { }
    public class RectMask2D : UIBehaviour { }

    public class LayoutElement : UIBehaviour
    {
        public float preferredHeight { get; set; }
        public float minHeight { get; set; }
    }

    public abstract class HorizontalOrVerticalLayoutGroup : UIBehaviour
    {
        public float spacing { get; set; }
        public bool childControlHeight { get; set; }
        public bool childControlWidth { get; set; }
        public bool childForceExpandHeight { get; set; }
        public bool childForceExpandWidth { get; set; }
    }

    public class VerticalLayoutGroup : HorizontalOrVerticalLayoutGroup { }

    public class ContentSizeFitter : UIBehaviour
    {
        public enum FitMode { Unconstrained, MinSize, PreferredSize }
        public FitMode verticalFit { get; set; }
    }
}

namespace UnityEngine.InputSystem
{
    using UnityEngine.InputSystem.Controls;

    public class Keyboard
    {
        public static Keyboard current => null;
        public KeyControl aKey => null;
        public KeyControl dKey => null;
        public KeyControl wKey => null;
        public KeyControl sKey => null;
        public KeyControl jKey => null;
        public KeyControl kKey => null;
        public KeyControl lKey => null;
        public KeyControl uKey => null;
        public KeyControl iKey => null;
        public KeyControl oKey => null;
        public KeyControl spaceKey => null;
        public KeyControl leftArrowKey => null;
        public KeyControl rightArrowKey => null;
        public KeyControl upArrowKey => null;
        public KeyControl downArrowKey => null;
    }

    public class Gamepad
    {
        public static Gamepad current => null;
        public StickControl leftStick => null;
        public DpadControl dpad => null;
        public ButtonControl buttonSouth => null;
        public ButtonControl buttonWest => null;
        public ButtonControl buttonEast => null;
        public ButtonControl buttonNorth => null;
        public ButtonControl leftShoulder => null;
        public ButtonControl rightShoulder => null;
        public ButtonControl leftTrigger => null;
        public ButtonControl rightTrigger => null;
    }
}

namespace UnityEngine.InputSystem.Controls
{
    public class ButtonControl { public bool isPressed => false; }
    public class KeyControl : ButtonControl { }
    public class Vector2Control { public Vector2 ReadValue() => default; }
    public class StickControl : Vector2Control { }
    public class DpadControl : Vector2Control { }
}

namespace UnityEngine.InputSystem.UI
{
    public class InputSystemUIInputModule : UnityEngine.EventSystems.BaseInputModule { }
}

namespace TMPro
{
    using UnityEngine.UI;

    public enum TextAlignmentOptions { TopLeft = 257, Top = 258, Left = 513, Center = 514, Right = 516, BottomLeft = 1025, Bottom = 1026 }

    [System.Flags]
    public enum FontStyles { Normal = 0, Bold = 1, Italic = 2 }

    public enum TextWrappingModes { NoWrap = 0, Normal = 1 }

    public abstract class TMP_Text : MaskableGraphic
    {
        public string text { get; set; }
        public float fontSize { get; set; }
        public TextAlignmentOptions alignment { get; set; }
        public FontStyles fontStyle { get; set; }
        public TextWrappingModes textWrappingMode { get; set; }
        public float alpha { get; set; }
    }

    public class TextMeshProUGUI : TMP_Text { }

    public class TMP_InputField : Selectable
    {
        public enum ContentType { Standard, Autocorrected, IntegerNumber, DecimalNumber, Alphanumeric }
        public RectTransform textViewport { get; set; }
        public TMP_Text textComponent { get; set; }
        public Graphic placeholder { get; set; }
        public ContentType contentType { get; set; }
        public string text { get; set; }
    }

    public static class TMP_PackageResourceImporter
    {
        public static void ImportResources(bool importEssentials, bool importExamples, bool interactive) { }
    }
}
