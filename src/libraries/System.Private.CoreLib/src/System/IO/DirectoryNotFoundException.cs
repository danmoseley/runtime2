// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;

namespace System.IO
{
    /*
     * Thrown when trying to access a directory that doesn't exist on disk.
     * From COM Interop, this exception is thrown for 2 HRESULTS:
     * the Win32 errorcode-as-HRESULT ERROR_PATH_NOT_FOUND (0x80070003)
     * and STG_E_PATHNOTFOUND (0x80030003).
     */
    [Serializable]
    [TypeForwardedFrom("mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089")]
    public class DirectoryNotFoundException : IOException
    {
        public DirectoryNotFoundException()
            : base(SR.Arg_DirectoryNotFoundException)
        {
            HResult = HResults.COR_E_DIRECTORYNOTFOUND;
        }

        public DirectoryNotFoundException(string? message)
            : base(message ?? SR.Arg_DirectoryNotFoundException)
        {
            HResult = HResults.COR_E_DIRECTORYNOTFOUND;
        }

        public DirectoryNotFoundException(string? message, Exception? innerException)
            : base(message ?? SR.Arg_DirectoryNotFoundException, innerException)
        {
            HResult = HResults.COR_E_DIRECTORYNOTFOUND;
        }

        public DirectoryNotFoundException(string? message, string? directoryPath)
            : base(message)
        {
            HResult = HResults.COR_E_DIRECTORYNOTFOUND;
            DirectoryPath = directoryPath;
        }

        public DirectoryNotFoundException(string? message, string? directoryPath, Exception? innerException)
            : base(message, innerException)
        {
            HResult = HResults.COR_E_DIRECTORYNOTFOUND;
            DirectoryPath = directoryPath;
        }

        public override string Message
        {
            get
            {
                SetMessageField();
                return _message ?? SR.Arg_DirectoryNotFoundException;
            }
        }

        private void SetMessageField()
        {
            if (_message == null && DirectoryPath != null)
            {
                _message = SR.Format(SR.IO_PathNotFound_Path, DirectoryPath);
            }
        }

        public string? DirectoryPath { get; }

        public override string ToString()
        {
            string s = GetType().ToString() + ": " + Message;

            if (!string.IsNullOrEmpty(DirectoryPath))
                s += Environment.NewLineConst + SR.Format(SR.IO_DirectoryName_Name, DirectoryPath);

            if (InnerException != null)
                s += Environment.NewLineConst + InnerExceptionPrefix + InnerException.ToString();

            if (StackTrace != null)
                s += Environment.NewLineConst + StackTrace;

            return s;
        }

        [Obsolete(Obsoletions.LegacyFormatterImplMessage, DiagnosticId = Obsoletions.LegacyFormatterImplDiagId, UrlFormat = Obsoletions.SharedUrlFormat)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        protected DirectoryNotFoundException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }
}
