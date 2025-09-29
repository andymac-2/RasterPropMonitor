namespace JSI
{
    /// <summary>
    /// This class exists to provide a base class that RasterPropMonitorComputer
    /// manages for tracking various built-in plugin action handlers.
    /// </summary>
    internal interface IVariable
    {
        object GetValue();
        bool Changed(object oldValue);
        bool IsConstant();
    }
}
