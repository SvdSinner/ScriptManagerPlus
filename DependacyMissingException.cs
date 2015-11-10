using System;

namespace ScriptManagerPlus
{
    public class DependacyMissingException : Exception
    {
        public DependacyMissingException(string message) : base(message)
        { }
    }
}

