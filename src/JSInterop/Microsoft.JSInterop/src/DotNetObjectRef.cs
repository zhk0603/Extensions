// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;

namespace Microsoft.JSInterop
{
    /// <summary>
    /// Wraps a JS interop argument, indicating that the value should not be serialized as JSON
    /// but instead should be passed as a reference.
    ///
    /// To avoid leaking memory, the reference must later be disposed by JS code or by .NET code.
    /// </summary>
    public class DotNetObjectRef : IDisposable, IConvertible
    {
        /// <summary>
        /// Gets the object instance represented by this wrapper.
        /// </summary>
        public object Value { get; }

        // We track an associated IJSRuntime purely so that this class can be IDisposable
        // in the normal way. Developers are more likely to use objectRef.Dispose() than
        // some less familiar API such as JSRuntime.Current.UntrackObjectRef(objectRef).
        private IJSRuntime _attachedToRuntime;

        /// <summary>
        /// Constructs an instance of <see cref="DotNetObjectRef"/>.
        /// </summary>
        /// <param name="value">The value being wrapped.</param>
        public DotNetObjectRef(object value)
        {
            Value = value;
        }

        /// <summary>
        /// Ensures the <see cref="DotNetObjectRef"/> is associated with the specified <see cref="IJSRuntime"/>.
        /// Developers do not normally need to invoke this manually, since it is called automatically by
        /// framework code.
        /// </summary>
        /// <param name="runtime">The <see cref="IJSRuntime"/>.</param>
        public void EnsureAttachedToJsRuntime(IJSRuntime runtime)
        {
            // The reason we populate _attachedToRuntime here rather than in the constructor
            // is to ensure developers can't accidentally try to reuse DotNetObjectRef across
            // different IJSRuntime instances. This method gets called as part of serializing
            // the DotNetObjectRef during an interop call.

            var existingRuntime = Interlocked.CompareExchange(ref _attachedToRuntime, runtime, null);
            if (existingRuntime != null && existingRuntime != runtime)
            {
                throw new InvalidOperationException($"The {nameof(DotNetObjectRef)} is already associated with a different {nameof(IJSRuntime)}. Do not attempt to re-use {nameof(DotNetObjectRef)} instances with multiple {nameof(IJSRuntime)} instances.");
            }
        }

        /// <summary>
        /// Stops tracking this object reference, allowing it to be garbage collected
        /// (if there are no other references to it). Once the instance is disposed, it
        /// can no longer be used in interop calls from JavaScript code.
        /// </summary>
        public void Dispose()
        {
            _attachedToRuntime?.UntrackObjectRef(this);
        }

        #region IConvertible
        TypeCode IConvertible.GetTypeCode() => TypeCode.String;

        bool IConvertible.ToBoolean(IFormatProvider provider)
        {
            throw new NotSupportedException();
        }

        byte IConvertible.ToByte(IFormatProvider provider)
        {
            throw new NotSupportedException();
        }

        char IConvertible.ToChar(IFormatProvider provider)
        {
            throw new NotSupportedException();
        }

        DateTime IConvertible.ToDateTime(IFormatProvider provider)
        {
            throw new NotSupportedException();
        }

        decimal IConvertible.ToDecimal(IFormatProvider provider)
        {
            throw new NotSupportedException();
        }

        double IConvertible.ToDouble(IFormatProvider provider)
        {
            throw new NotSupportedException();
        }

        short IConvertible.ToInt16(IFormatProvider provider)
        {
            throw new NotSupportedException();
        }

        int IConvertible.ToInt32(IFormatProvider provider)
        {
            throw new NotSupportedException();
        }

        long IConvertible.ToInt64(IFormatProvider provider)
        {
            throw new NotSupportedException();
        }

        sbyte IConvertible.ToSByte(IFormatProvider provider)
        {
            throw new NotSupportedException();
        }

        float IConvertible.ToSingle(IFormatProvider provider)
        {
            throw new NotSupportedException();
        }

        string IConvertible.ToString(IFormatProvider provider)
        {
            var jsRuntime = (JSRuntimeBase)JSRuntime.Current;
            var objectId = jsRuntime.TrackDotNetObject(this, out var id);
            return JSRuntimeBase.DotNetObjectPrefix + objectId;
        }

        object IConvertible.ToType(Type conversionType, IFormatProvider provider)
        {
            throw new NotSupportedException();
        }

        ushort IConvertible.ToUInt16(IFormatProvider provider)
        {
            throw new NotSupportedException();
        }

        uint IConvertible.ToUInt32(IFormatProvider provider)
        {
            throw new NotSupportedException();
        }

        ulong IConvertible.ToUInt64(IFormatProvider provider)
        {
            throw new NotSupportedException();
        }
        #endregion
    }
}
