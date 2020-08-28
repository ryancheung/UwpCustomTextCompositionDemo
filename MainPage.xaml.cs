using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using Windows.System;
using Windows.Graphics.Display;
using Windows.UI.Core;
using Windows.UI.Text.Core;
using Windows.UI.ViewManagement;

// https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x804 上介绍了“空白页”项模板

namespace UwpCustomTextCompositionDemo
{
    /// <summary>
    /// 可用于自身或导航至 Frame 内部的空白页。
    /// </summary>
    public sealed partial class MainPage : Page
    {
        private string _inputContent = string.Empty;

        // The _editContext lets us communicate with the input system.
        CoreTextEditContext _editContext;

        // If the _selection starts and ends at the same point,
        // then it represents the location of the caret (insertion point).
        CoreTextRange _selection;

        // _internalFocus keeps track of whether our control acts like it has focus.
        bool _internalFocus = false;

        // The input pane object indicates the visibility of the on screen keyboard.
        // Apps can also ask the keyboard to show or hide.
        InputPane _inputPane;

        // The CoreWindow that contains our control.
        CoreWindow _coreWindow;

        int _virtualKeyboardHeight;

        bool _compositionStarted;
        DispatcherQueueTimer _timer;
        string _inputBuffer = string.Empty;
        string _lastResultText = string.Empty;

        Rect _textInputRect;

        public MainPage()
        {
            this.InitializeComponent();

            var textBoxCandidates = this.FindName("candidateBox") as TextBox;
            textBoxCandidates.IsEnabled = false;

            _coreWindow = CoreWindow.GetForCurrentThread();
            _coreWindow.KeyDown += CoreWindow_KeyDown;
            _coreWindow.CharacterReceived += CoreWindow_CharacterReceived;

            // Create a CoreTextEditContext for our custom edit control.
            CoreTextServicesManager manager = CoreTextServicesManager.GetForCurrentView();
            _editContext = manager.CreateEditContext();

            // Get the Input Pane so we can programmatically hide and show it.
            _inputPane = InputPane.GetForCurrentView();
            _inputPane.Showing += (o, e) => _virtualKeyboardHeight = (int)e.OccludedRect.Height;
            _inputPane.Hiding += (o, e) => _virtualKeyboardHeight = 0;

            // For demonstration purposes, this sample sets the Input Pane display policy to Manual
            // so that it can manually show the software keyboard when the control gains focus and
            // dismiss it when the control loses focus. If you leave the policy as Automatic, then
            // the system will hide and show the Input Pane for you. Note that on Desktop, you will
            // need to implement the UIA text pattern to get expected automatic behavior.
            _editContext.InputPaneDisplayPolicy = CoreTextInputPaneDisplayPolicy.Manual;

            // Set the input scope to Text because this text box is for any text.
            // This also informs software keyboards to show their regular
            // text entry layout.  There are many other input scopes and each will
            // inform a keyboard layout and text behavior.
            _editContext.InputScope = CoreTextInputScope.Text;

            // The system raises this event to request a specific range of text.
            _editContext.TextRequested += EditContext_TextRequested;

            // The system raises this event to request the current selection.
            _editContext.SelectionRequested += EditContext_SelectionRequested;

            // The system raises this event when it wants the edit control to remove focus.
            _editContext.FocusRemoved += EditContext_FocusRemoved;

            // The system raises this event to update text in the edit control.
            _editContext.TextUpdating += EditContext_TextUpdating;

            // The system raises this event to change the selection in the edit control.
            _editContext.SelectionUpdating += EditContext_SelectionUpdating;

            // The system raises this event to request layout information.
            // This is used to help choose a position for the IME candidate window.
            _editContext.LayoutRequested += EditContext_LayoutRequested;

            // The system raises this event to notify the edit control
            // that the string composition has started.
            _editContext.CompositionStarted += EditContext_CompositionStarted;

            // The system raises this event to notify the edit control
            // that the string composition is finished.
            _editContext.CompositionCompleted += EditContext_CompositionCompleted;

            // The system raises this event when the NotifyFocusLeave operation has
            // completed. Our sample does not use this event.
            // _editContext.NotifyFocusLeaveCompleted += EditContext_NotifyFocusLeaveCompleted;

            _timer = _coreWindow.DispatcherQueue.CreateTimer();
            _timer.Interval = new TimeSpan(0, 0, 0, 0, 10);
            _timer.IsRepeating = false;
            _timer.Tick += (o, e) =>
            {
                //Debug.WriteLine("Result text: {0}", (object)_lastResultText);
                foreach (var c in _lastResultText)
                    OnTextInput(c);
            };
        }

        private void CoreWindow_KeyDown(Windows.UI.Core.CoreWindow sender, Windows.UI.Core.KeyEventArgs args)
        {
            if (args.VirtualKey == Windows.System.VirtualKey.F1)
            {
                if (!_internalFocus)
                {
                    // The user tapped inside the control. Give it focus.
                    SetInternalFocus();

                    // Tell XAML that this element has focus, so we don't have two
                    // focus elements. That is the extent of our integration with XAML focus.
                    Focus(FocusState.Programmatic);

                    // A more complete custom control would move the caret to the
                    // pointer position. It would also provide some way to select
                    // text via touch. We do neither in this sample.
                }
                else
                {
                    // The user tapped outside the control. Remove focus.
                    RemoveInternalFocus();
                }
            }
        }

        private void CoreWindow_CharacterReceived(Windows.UI.Core.CoreWindow sender, CharacterReceivedEventArgs args)
        {
            if (_internalFocus)
                OnTextInput((char)args.KeyCode);
        }

        void SetInternalFocus()
        {
            if (!_internalFocus)
            {
                // Update internal notion of focus.
                _internalFocus = true;

                // Notify the CoreTextEditContext that the edit context has focus,
                // so it should start processing text input.
                _editContext.NotifyFocusEnter();
            }

            // Ask the software keyboard to show.  The system will ultimately decide if it will show.
            // For example, it will not show if there is a keyboard attached.
            _inputPane.TryShow();

        }

        void RemoveInternalFocus()
        {
            if (_internalFocus)
            {
                //Notify the system that this edit context is no longer in focus
                _editContext.NotifyFocusLeave();

                RemoveInternalFocusWorker();
            }
        }

        void RemoveInternalFocusWorker()
        {
            //Update the internal notion of focus
            _internalFocus = false;

            // Ask the software keyboard to dismiss.
            _inputPane.TryHide();
        }

        void EditContext_FocusRemoved(CoreTextEditContext sender, object args)
        {
            RemoveInternalFocusWorker();
        }

        static Rect GetElementRect(FrameworkElement element)
        {
            GeneralTransform transform = element.TransformToVisual(null);
            Point point = transform.TransformPoint(new Point());
            return new Rect(point, new Size(element.ActualWidth, element.ActualHeight));
        }

        // Replace the text in the specified range.
        void ReplaceText(CoreTextRange modifiedRange, string text)
        {
            // Modify the internal text store.
            _inputBuffer = _inputBuffer.Substring(0, modifiedRange.StartCaretPosition) +
              text +
              _inputBuffer.Substring(modifiedRange.EndCaretPosition);

            // Move the caret to the end of the replacement text.
            _selection.StartCaretPosition = modifiedRange.StartCaretPosition + text.Length;
            _selection.EndCaretPosition = _selection.StartCaretPosition;

            // Update the selection of the edit context.  There is no need to notify the system
            // of the selection change because we are going to call NotifyTextChanged soon.
            SetSelection(_selection);

            // Let the CoreTextEditContext know what changed.
            _editContext.NotifyTextChanged(modifiedRange, text.Length, _selection);
        }

        // Change the selection without notifying CoreTextEditContext of the new selection.
        void SetSelection(CoreTextRange selection)
        {
            // Modify the internal selection.
            _selection = selection;
        }

        // Return the specified range of text. Note that the system may ask for more text
        // than exists in the text buffer.
        void EditContext_TextRequested(CoreTextEditContext sender, CoreTextTextRequestedEventArgs args)
        {
            CoreTextTextRequest request = args.Request;
            request.Text = _inputBuffer.Substring(
                request.Range.StartCaretPosition,
                Math.Min(request.Range.EndCaretPosition, _inputBuffer.Length) - request.Range.StartCaretPosition);
        }

        // Return the current selection.
        void EditContext_SelectionRequested(CoreTextEditContext sender, CoreTextSelectionRequestedEventArgs args)
        {
            CoreTextSelectionRequest request = args.Request;
            request.Selection = _selection;
        }

        void EditContext_TextUpdating(CoreTextEditContext sender, CoreTextTextUpdatingEventArgs args)
        {
            if (!_compositionStarted) // Only update text  when composition is started.
            {
                args.Result = CoreTextTextUpdatingResult.Failed;
                return;
            }

            CoreTextRange range = args.Range;
            string newText = args.Text;
            CoreTextRange newSelection = args.NewSelection;

            // Modify the internal text store.
            _inputBuffer = _inputBuffer.Substring(0, range.StartCaretPosition) +
                newText +
                _inputBuffer.Substring(Math.Min(_inputBuffer.Length, range.EndCaretPosition));

            // You can set the proper font or direction for the updated text based on the language by checking
            // args.InputLanguage.  We will not do that in this sample.

            // Modify the current selection.
            newSelection.EndCaretPosition = newSelection.StartCaretPosition;

            // Update the selection of the edit context. There is no need to notify the system
            // because the system itself changed the selection.
            SetSelection(newSelection);

            //var compStr = _inputBuffer;
            //compStr = compStr.Insert(_selection.StartCaretPosition, "|");
            //Debug.WriteLine("composition text: {0}, cursor pos: {1}", (object)compStr, _selection.StartCaretPosition);
            OnTextComposition(_inputBuffer, _selection.StartCaretPosition);
        }

        void EditContext_SelectionUpdating(CoreTextEditContext sender, CoreTextSelectionUpdatingEventArgs args)
        {
            // Set the new selection to the value specified by the system.
            CoreTextRange range = args.Selection;

            // Update the selection of the edit context. There is no need to notify the system
            // because the system itself changed the selection.
            SetSelection(range);

            //var compStr = _inputBuffer;
            //compStr = compStr.Insert(_selection.StartCaretPosition, "|");
            //Debug.WriteLine("composition text: {0}, cursor pos: {1}", (object)compStr, _selection.StartCaretPosition);

            OnTextComposition(_inputBuffer, _selection.StartCaretPosition);
        }

        static Rect ScaleRect(Rect rect, double scale)
        {
            rect.X *= scale;
            rect.Y *= scale;
            rect.Width *= scale;
            rect.Height *= scale;
            return rect;
        }

        void EditContext_LayoutRequested(CoreTextEditContext sender, CoreTextLayoutRequestedEventArgs args)
        {
            CoreTextLayoutRequest request = args.Request;

            request.LayoutBounds.TextBounds = _textInputRect;
        }

        // This indicates that an IME has started composition.  If there is no handler for this event,
        // then composition will not start.
        void EditContext_CompositionStarted(CoreTextEditContext sender, CoreTextCompositionStartedEventArgs args)
        {
            _compositionStarted = true;
        }

        void EditContext_CompositionCompleted(CoreTextEditContext sender, CoreTextCompositionCompletedEventArgs args)
        {
            _lastResultText = _inputBuffer;
            _coreWindow.DispatcherQueue.TryEnqueue(() => 
            {
                ReplaceText(new CoreTextRange { StartCaretPosition = 0, EndCaretPosition = _inputBuffer.Length }, string.Empty);
            });

            if (!_timer.IsRunning)
                _timer.Start();

            OnTextComposition(string.Empty, 0);

            _compositionStarted = false;
        }


        // Helper function to put a zero-width non-breaking space at the end of a string.
        // This prevents TextBlock from trimming trailing spaces.
        static string PreserveTrailingSpaces(string s)
        {
            return s + "\ufeff";
        }

        private void OnTextInput(char character)
        {
            switch (character)
            {
                case '\b':
                    if (_inputContent.Length > 0)
                        _inputContent = _inputContent.Remove(_inputContent.Length - 1, 1);
                    break;
                case '\r':
                    _inputContent = "";
                    break;
                default:
                    _inputContent += character;
                    break;
            }

            var resultTextLabel = (this.FindName("resultTextLabel") as TextBlock);

            //Console.WriteLine("inputContent: {0}", _inputContent);
            resultTextLabel.Text = _inputContent;
        }

        private void OnTextComposition(string compositionText, int cursorPosition)
        {
            var str = compositionText.ToString();
            str = str.Insert(cursorPosition, "|");
            var compStringLabel = this.FindName("compStringLabel") as TextBlock;
            compStringLabel.Text = str;

            Rect windowBounds = Window.Current.CoreWindow.Bounds;

            var ttv = compStringLabel.TransformToVisual(Window.Current.Content);
            Point compositionTextCoords = ttv.TransformPoint(new Point(0, 0));
            _textInputRect = new Rect(compositionTextCoords.X + compStringLabel.ActualWidth, compositionTextCoords.Y, 0, compStringLabel.ActualHeight);
            _textInputRect.X += windowBounds.X;
            _textInputRect.Y += windowBounds.Y;

            // Finally, scale up to raw pixels.
            double scaleFactor = DisplayInformation.GetForCurrentView().RawPixelsPerViewPixel;
            _textInputRect = ScaleRect(_textInputRect, scaleFactor);
        }
    }
}
