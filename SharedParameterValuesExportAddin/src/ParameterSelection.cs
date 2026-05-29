namespace SharedParameterValuesExportAddin
{
    public class ParameterSelection
    {
        public ParameterSelection(string name, ParameterScope scope)
            : this(name, scope, ParameterOrigin.Family)
        {
        }

        public ParameterSelection(string name, ParameterScope scope, ParameterOrigin origin)
        {
            Name = name;
            Scope = scope;
            Origin = origin;
        }

        public string Name { get; private set; }
        public ParameterScope Scope { get; private set; }
        public ParameterOrigin Origin { get; private set; }

        public string DisplayName
        {
            get
            {
                return string.Format("{0} [{1}]", Name, Scope);
            }
        }
    }
}
