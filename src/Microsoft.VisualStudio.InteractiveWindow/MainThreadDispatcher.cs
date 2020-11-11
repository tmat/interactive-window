// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Windows;
using System.Windows.Threading;

namespace Microsoft.VisualStudio.InteractiveWindow
{
    internal sealed class MainThreadDispatcher : IMainThreadDispatcher
    {
        private readonly Dispatcher _dispatcher;

        public MainThreadDispatcher()
        {
            _dispatcher = new FrameworkElement().Dispatcher;
        }
        
        public bool IsExecutingOnMainThread
            => _dispatcher.CheckAccess();

        public T ExecuteOnMainThread<T>(Func<T> func)
        {
            if (!IsExecutingOnMainThread)
            {
                return _dispatcher.Invoke(func); // Safe because of dispatch.
            }

            return func(); // Safe because of check.
        }

        public void ExecuteOnMainThread(Action action)
        {
            if (!IsExecutingOnMainThread)
            {
                _dispatcher.Invoke(action); // Safe because of dispatch.
                return;
            }

            action(); // Safe because of check.
        }
    }
}
