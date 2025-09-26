namespace PortTown01.Core
{

    public interface ISimSystem
    {

        string Name { get; } 

        void Tick(World world, int tickIndex, float dt);
        
    }
}
