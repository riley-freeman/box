namespace Crayon.Box.Messages;

public struct RequestResourceInformation(string resourceName)
{
    public string ResourceName { get; set; } = resourceName;
}
