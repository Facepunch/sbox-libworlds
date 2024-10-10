
namespace Sandbox.Worlds;

/// <summary>
/// Handles loading in and unloading cells of a <see cref="StreamingWorld"/>,
/// when implemented in a <see cref="Component"/>.
/// </summary>
public interface ICellLoader
{
	/// <summary>
	/// <para>
	/// Called when a cell in a <see cref="StreamingWorld"/> is loading in. This is a good
	/// place to load stuff from disk, or do some procedural generation.
	/// </para>
	/// <para>
	/// Cells are identified by a <see cref="WorldCell.Index"/>, which includes
	/// which level of detail it belongs to. Level <c>0</c> is the highest level of detail.
	/// </para>
	/// </summary>
	/// <param name="cell">Cell that wants to load in.</param>
	void LoadCell( WorldCell cell );

	/// <summary>
	/// Called when a cell in a <see cref="StreamingWorld"/> is unloading. This is where you
	/// could save objects in the cell to disk, then destroy them. The cell object itself will
	/// be destroyed for you after this method.
	/// </summary>
	void UnloadCell( WorldCell cell );
}
