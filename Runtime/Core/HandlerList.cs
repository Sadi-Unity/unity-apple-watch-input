using System;
using UnityEngine;

namespace WatchInput.Core
{
    /// <summary>
    /// Thread-safe multicast handler list with zero GC allocation on invoke.
    /// Allocations occur strictly upon subscriber addition or removal.
    /// Each subscriber is isolated in its own try/catch block so exceptions do not abort remaining subscribers.
    /// </summary>
    internal sealed class HandlerList
    {
        private Action[] _handlers = Array.Empty<Action>();
        private readonly object _lock = new object();

        public void Add(Action handler)
        {
            if (handler == null) return;
            lock (_lock)
            {
                var next = new Action[_handlers.Length + 1];
                Array.Copy(_handlers, next, _handlers.Length);
                next[_handlers.Length] = handler;
                _handlers = next;
            }
        }

        public void Remove(Action handler)
        {
            if (handler == null) return;
            lock (_lock)
            {
                int index = Array.IndexOf(_handlers, handler);
                if (index < 0) return;

                var next = new Action[_handlers.Length - 1];
                for (int i = 0, j = 0; i < _handlers.Length; i++)
                {
                    if (i != index)
                    {
                        next[j++] = _handlers[i];
                    }
                }
                _handlers = next;
            }
        }

        public void Invoke()
        {
            Action[] snapshot = _handlers;
            for (int i = 0; i < snapshot.Length; i++)
            {
                try
                {
                    snapshot[i]?.Invoke();
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _handlers = Array.Empty<Action>();
            }
        }
    }

    /// <summary>
    /// Generic thread-safe multicast handler list with zero GC allocation on invoke.
    /// </summary>
    internal sealed class HandlerList<T>
    {
        private Action<T>[] _handlers = Array.Empty<Action<T>>();
        private readonly object _lock = new object();

        public void Add(Action<T> handler)
        {
            if (handler == null) return;
            lock (_lock)
            {
                var next = new Action<T>[_handlers.Length + 1];
                Array.Copy(_handlers, next, _handlers.Length);
                next[_handlers.Length] = handler;
                _handlers = next;
            }
        }

        public void Remove(Action<T> handler)
        {
            if (handler == null) return;
            lock (_lock)
            {
                int index = Array.IndexOf(_handlers, handler);
                if (index < 0) return;

                var next = new Action<T>[_handlers.Length - 1];
                for (int i = 0, j = 0; i < _handlers.Length; i++)
                {
                    if (i != index)
                    {
                        next[j++] = _handlers[i];
                    }
                }
                _handlers = next;
            }
        }

        public void Invoke(T arg)
        {
            Action<T>[] snapshot = _handlers;
            for (int i = 0; i < snapshot.Length; i++)
            {
                try
                {
                    snapshot[i]?.Invoke(arg);
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _handlers = Array.Empty<Action<T>>();
            }
        }
    }
}
