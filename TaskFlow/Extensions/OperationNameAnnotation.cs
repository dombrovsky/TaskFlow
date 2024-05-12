namespace System.Threading.Tasks.Flow
{
    public sealed class OperationNameAnnotation : IOperationAnnotation
    {
        public OperationNameAnnotation(string operationName)
        {
            OperationName = operationName;
        }

        public string OperationName { get; }
    }
}