namespace ReciteHelper.Model
{
    /// <summary>
    /// Represents the result of an operation, including its success status, output value, and error information.
    /// </summary>
    /// <remarks>Use this class to encapsulate the outcome of an operation, providing a consistent way to
    /// indicate success or failure and to carry either a result or an error message. The static methods <see
    /// cref="Success(object)"/> and <see cref="Failed(string)"/> can be used to create instances representing
    /// successful or failed executions, respectively.</remarks>
    /// <typeparam name="TOut">The type of the output value returned by the operation.</typeparam>
    public class ExecutionResult<TOut>
    {
        public bool IsSuccess { get; set; }
        public TOut? Result { get; set; }
        public string? ErrorMessage { get; set; }

        public static ExecutionResult<TOut> Success(TOut result)
            => new ExecutionResult<TOut> { IsSuccess = true, Result = result };

        public static ExecutionResult<TOut> Failed(string error)
            => new ExecutionResult<TOut> { IsSuccess = false, ErrorMessage = error };
    }
}
