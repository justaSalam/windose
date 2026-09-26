using System;
using System.Collections.Generic;
using System.Text;

namespace Windose.System.ABI.WIN
{
    public enum AbiError : int
    {
        Success = 0,

        InvalidArgument = 1,
        InvalidHandle = 2,
        NotFound = 3,
        AccessDenied = 4,
        OutOfMemory = 5,
        Unsupported = 6,
        BufferTooSmall = 7
    }
}
