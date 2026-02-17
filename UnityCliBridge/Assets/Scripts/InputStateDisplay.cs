using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

public class InputStateDisplay : MonoBehaviour
{
    private readonly StringBuilder _builder = new StringBuilder(256);
    private Keyboard _keyboard;
    private Mouse _mouse;
    private Gamepad _gamepad;
    private Touchscreen _touchscreen;

    private void Update()
    {
        _keyboard = Keyboard.current;
        _mouse = Mouse.current;
        _gamepad = Gamepad.current;
        _touchscreen = Touchscreen.current;
    }

    private void OnGUI()
    {
        _builder.Clear();
        _builder.AppendLine("Input Monitor");

        if (_keyboard != null)
        {
            var pressed = string.Join(",", _keyboard.allKeys.Where(k => k != null && k.isPressed).Select(k => k.displayName));
            _builder.AppendLine($"Keyboard: {pressed}");
        }
        else
        {
            _builder.AppendLine("Keyboard: (none)");
        }

        if (_mouse != null)
        {
            var pos = _mouse.position.ReadValue();
            _builder.AppendLine($"Mouse Pos: {pos} L={_mouse.leftButton.isPressed} R={_mouse.rightButton.isPressed}");
        }
        else
        {
            _builder.AppendLine("Mouse: (none)");
        }

        if (_gamepad != null)
        {
            _builder.AppendLine($"Gamepad LS: {_gamepad.leftStick.ReadValue()} RS: {_gamepad.rightStick.ReadValue()} A={_gamepad.buttonSouth.isPressed} B={_gamepad.buttonEast.isPressed}");
        }
        else
        {
            _builder.AppendLine("Gamepad: (none)");
        }

        if (_touchscreen != null)
        {
            var touches = _touchscreen.touches.Where(t => t.isInProgress).Select(t => t.position.ReadValue()).ToArray();
            _builder.AppendLine($"Touches: {touches.Length}");
            for (var i = 0; i < touches.Length; i++)
            {
                _builder.AppendLine($"  {i}: {touches[i]}");
            }
        }
        else
        {
            _builder.AppendLine("Touchscreen: (none)");
        }

        GUI.Label(new Rect(10, 10, 600, 400), _builder.ToString());
    }
}
