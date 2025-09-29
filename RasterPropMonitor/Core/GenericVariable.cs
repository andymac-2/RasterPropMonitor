using System;

namespace JSI
{
    public class GenericVariable : IVariable
    {
        private Func<object> Generator;

        internal GenericVariable(Func<object> generator)
        {
            Generator = generator;
        }

        object IVariable.GetValue()
        {
            return Generator();
        }

        bool IVariable.Changed(object oldValue)
        {
            return true;
        }

        bool IVariable.IsConstant()
        {
            return false;
        }
    }
}