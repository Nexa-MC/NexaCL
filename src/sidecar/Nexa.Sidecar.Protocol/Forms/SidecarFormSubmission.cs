namespace Nexa.Sidecar.Protocol;

/// <summary>Document-local drafts captured when an admitted NXML command is invoked.</summary>
public sealed record SidecarFormSubmission(string Argument, SidecarFormField[] Fields);

/// <summary>A named input draft, encoded as strict UTF-8 independently of scalar string limits.</summary>
public sealed record SidecarFormField(string Key, byte[] Utf8Value);
