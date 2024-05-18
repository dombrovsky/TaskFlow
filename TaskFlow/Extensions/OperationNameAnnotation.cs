namespace System.Threading.Tasks.Flow
{
    public sealed class OperationNameAnnotation : IOperationAnnotation
    {
        internal OperationNameAnnotation(string operationName)
        {
            OperationName = operationName;
        }

        public string OperationName { get; }
    }
}