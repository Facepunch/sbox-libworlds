
namespace Sandbox.Worlds;

/// <summary>
/// Tell <see cref="StreamingWorld"/>s to load cells around this object.
/// You usually won't need this, because they load cells around any active cameras anyway.
/// </summary>
[Icon( "filter_center_focus" )]
public sealed class LoadOrigin : Component
{
	/// <summary>
	/// Optional maximum detail level required for this load origin.
	/// </summary>
	[Property]
	public int? MaxLevel { get; set; }
}
