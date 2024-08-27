
namespace Sandbox.Worlds;

public interface ICellLoader
{
	void LoadCell( WorldCell cell );
	void UnloadCell( WorldCell cell );
}
