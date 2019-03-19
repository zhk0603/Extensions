// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.JSInterop.Internal;

namespace Microsoft.JSInterop
{
    /// <summary>
    /// Abstract base class for a JavaScript runtime.
    /// </summary>
    public abstract class JSRuntimeBase : IJSRuntime
    {
        internal const string DotNetObjectPrefix = "__dotNetObject:";
        private long _nextPendingTaskId = 1; // Start at 1 because zero signals "no response needed"
        private readonly ConcurrentDictionary<long, (object, Type)> _pendingTasks
            = new ConcurrentDictionary<long, (object, Type)>();

        private readonly object _storageLock = new object();
        private long _nextId = 1; // Start at 1, because 0 signals "no object"
        private readonly Dictionary<long, DotNetObjectRef> _trackedRefsById = new Dictionary<long, DotNetObjectRef>();
        private readonly Dictionary<DotNetObjectRef, long> _trackedIdsByRef = new Dictionary<DotNetObjectRef, long>();

        /// <inheritdoc />
        public void UntrackObjectRef(DotNetObjectRef dotNetObjectRef)
        {
            lock (_storageLock)
            {
                if (_trackedIdsByRef.TryGetValue(dotNetObjectRef, out var dotNetObjectId))
                {
                    _trackedRefsById.Remove(dotNetObjectId);
                    _trackedIdsByRef.Remove(dotNetObjectRef);
                }
            }
        }

        /// <inheritdoc />
        public void ReleaseDotNetObject(long dotNetObjectId)
        {
            lock (_storageLock)
            {
                if (_trackedRefsById.TryGetValue(dotNetObjectId, out var dotNetObjectRef))
                {
                    _trackedRefsById.Remove(dotNetObjectId);
                    _trackedIdsByRef.Remove(dotNetObjectRef);
                }
            }
        }

        /// <summary>
        /// Invokes the specified JavaScript function asynchronously.
        /// </summary>
        /// <typeparam name="TReturnType">The JSON-serializable return type.</typeparam>
        /// <param name="identifier">An identifier for the function to invoke. For example, the value <code>"someScope.someFunction"</code> will invoke the function <code>window.someScope.someFunction</code>.</param>
        /// <param name="args">JSON-serializable arguments.</param>
        /// <returns>An instance of <typeparamref name="TReturnType"/> obtained by JSON-deserializing the return value.</returns>
        public Task<TReturnType> InvokeAsync<TReturnType>(string identifier, params object[] args)
        {
            // We might consider also adding a default timeout here in case we don't want to
            // risk a memory leak in the scenario where the JS-side code is failing to complete
            // the operation.

            var taskId = Interlocked.Increment(ref _nextPendingTaskId);
            var tcs = new TaskCompletionSource<TReturnType>();
            _pendingTasks[taskId] = (tcs, typeof(TReturnType));

            try
            {
                var argsJson = args?.Length > 0
                    ? Json.Serialize(args)
                    : null;
                BeginInvokeJS(taskId, identifier, argsJson);
                return tcs.Task;
            }
            catch
            {
                _pendingTasks.TryRemove(taskId, out _);
                throw;
            }
        }

        /// <summary>
        /// Begins an asynchronous function invocation.
        /// </summary>
        /// <param name="asyncHandle">The identifier for the function invocation, or zero if no async callback is required.</param>
        /// <param name="identifier">The identifier for the function to invoke.</param>
        /// <param name="argsJson">A JSON representation of the arguments.</param>
        protected abstract void BeginInvokeJS(long asyncHandle, string identifier, string argsJson);

        internal void EndInvokeDotNet(string callId, bool success, object resultOrException)
        {
            // For failures, the common case is to call EndInvokeDotNet with the Exception object.
            // For these we'll serialize as something that's useful to receive on the JS side.
            // If the value is not an Exception, we'll just rely on it being directly JSON-serializable.
            if (!success && resultOrException is Exception)
            {
                resultOrException = resultOrException.ToString();
            }

            // We pass 0 as the async handle because we don't want the JS-side code to
            // send back any notification (we're just providing a result for an existing async call)
            BeginInvokeJS(0, "DotNet.jsCallDispatcher.endInvokeDotNetFromJS", Json.Serialize(new[]
            {
                callId,
                success,
                resultOrException
            }));
        }

        internal void EndInvokeJS(long asyncHandle, bool succeeded, JSAsyncCallResult callResult)
        {
            if (!_pendingTasks.TryRemove(asyncHandle, out var pendingTask))
            {
                throw new ArgumentException($"There is no pending task with handle '{asyncHandle}'.");
            }

            var (tcs, targetType) = pendingTask;
            if (succeeded)
            {
                var convertedResult = callResult.ResultOrExceptionJson == null ?
                    null :
                    JsonSerializer.Parse(callResult.ResultOrExceptionJson, targetType);

                TaskGenericsUtil.SetTaskCompletionSourceResult(tcs, convertedResult);
            }
            else
            {
                var convertedResult = JsonSerializer.Parse<string>(callResult.ResultOrExceptionJson);
                TaskGenericsUtil.SetTaskCompletionSourceException(tcs, new JSException(convertedResult));
            }
        }

        internal long TrackDotNetObject(DotNetObjectRef dotNetObjectRef, out long dotNetObjectId)
        {
            lock (_storageLock)
            {
                // Assign an ID only if it doesn't already have one
                if (!_trackedIdsByRef.TryGetValue(dotNetObjectRef, out dotNetObjectId))
                {
                    dotNetObjectId = _nextId++;
                    _trackedRefsById.Add(dotNetObjectId, dotNetObjectRef);
                    _trackedIdsByRef.Add(dotNetObjectRef, dotNetObjectId);
                }

                return dotNetObjectId;
            }
        }

        internal object FindDotNetObject(long dotNetObjectId)
        {
            lock (_storageLock)
            {
                return _trackedRefsById.TryGetValue(dotNetObjectId, out var dotNetObjectRef)
                    ? dotNetObjectRef.Value
                    : throw new ArgumentException($"There is no tracked object with id '{dotNetObjectId}'. Perhaps the reference was already released.", nameof(dotNetObjectId));
            }
        }

        internal bool TryReadDotNetObject(object value, out object @object)
        {
            if (value is string valueString && valueString.StartsWith(DotNetObjectPrefix, StringComparison.Ordinal))
            {
                var dotNetObjectId = long.Parse(valueString.Substring(DotNetObjectPrefix.Length));
                @object = FindDotNetObject(dotNetObjectId);
                return true;
            }

            @object = default;
            return false;
        }
    }
}
