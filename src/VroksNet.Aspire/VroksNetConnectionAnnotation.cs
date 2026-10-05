using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting;

/// <summary>
/// Records a connection declared with <c>WithConnection</c>: its position is the
/// <c>Provisioning__Connections__&lt;i&gt;</c> index, and its name must be unique.
/// </summary>
internal sealed record VroksNetConnectionAnnotation(string Name, VroksNetConnectionType Type) : IResourceAnnotation;
