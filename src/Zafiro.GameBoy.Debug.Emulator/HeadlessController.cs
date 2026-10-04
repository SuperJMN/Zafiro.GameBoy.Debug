using System.Collections.Generic;
using CoreBoy.controller;

namespace Zafiro.GameBoy.Debug.Emulator
{
    /// <summary>
    /// Headless <see cref="IController"/> whose pressed-button set is driven programmatically.
    /// Diffs the requested set against the current state and raises press/release events on the
    /// joypad listener, mirroring real input edges.
    /// </summary>
    internal sealed class HeadlessController : IController
    {
        private readonly HashSet<Button> _pressed = new HashSet<Button>();
        private IButtonListener _listener;

        public void SetButtonListener(IButtonListener listener) => _listener = listener;

        /// <summary>Sets the exact set of currently held buttons, emitting press/release edges.</summary>
        public void SetPressed(IEnumerable<Button> buttons)
        {
            var target = new HashSet<Button>(buttons);

            foreach (var button in new List<Button>(_pressed))
            {
                if (!target.Contains(button))
                {
                    _pressed.Remove(button);
                    _listener?.OnButtonRelease(button);
                }
            }

            foreach (var button in target)
            {
                if (_pressed.Add(button))
                {
                    _listener?.OnButtonPress(button);
                }
            }
        }
    }
}
