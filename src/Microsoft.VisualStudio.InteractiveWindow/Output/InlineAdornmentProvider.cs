// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using Microsoft.VisualStudio.Text.Editor;
using System;
using System.Windows.Threading;

namespace Microsoft.VisualStudio.InteractiveWindow
{
    internal sealed class InlineAdornmentProvider : IInlineAdornmentProvider
    {
        public void AddInlineAdornment(ITextView view, object uiElement)
        {
            // UIElement uiElement, RoutedEventHandler onLoaded
        }

        private void OnAdornmentLoaded(object source, EventArgs e)
        {
            // Make sure the caret line is rendered
            DoEvents();
            TextView.Caret.EnsureVisible();
        }

        private static void DoEvents()
        {
            var frame = new DispatcherFrame();

            _ = Dispatcher.CurrentDispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action<DispatcherFrame>(f => f.Continue = false),
                frame);

            Dispatcher.PushFrame(frame);
        }

        public void MinimizeLastInlineAdornment(ITextView view)
        {
            throw new System.NotImplementedException();
        }

        public void RemoveAllAdornments(ITextView view)
        {
            throw new System.NotImplementedException();
        }

        public void ZoomInlineAdornments(ITextView view, double zoomFactor)
        {
            throw new System.NotImplementedException();
        }
    }
}
