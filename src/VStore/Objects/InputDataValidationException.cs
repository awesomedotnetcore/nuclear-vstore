using System;

namespace NuClear.VStore.Objects
{
    public class InputDataValidationException : Exception
    {
        public InputDataValidationException(string message) : base(message)
        {
        }
    }
}