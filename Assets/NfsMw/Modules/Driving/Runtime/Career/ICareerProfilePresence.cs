namespace NfsMwRemaster.Driving
{
    public interface ICareerProfilePresence
    {
        bool TryExists(string profileId, out bool exists, out string failure);
    }
    public interface ICareerProfileCreation
    {
        bool TryCreate(string profileId, string serializedProfile, out string failure);
    }
}
