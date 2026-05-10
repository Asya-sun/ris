namespace Worker.Exceptions;

public class BomDetectedException : Exception
{
    public BomDetectedException(string message) : base(message) { }
    public BomDetectedException(string message, Exception inner) : base(message, inner) { }
}