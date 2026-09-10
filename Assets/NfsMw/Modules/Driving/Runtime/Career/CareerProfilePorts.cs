namespace NfsMwRemaster.Driving
{
    /// <summary>
    /// UI and flow-facing profile contract. Callers do not need to know which
    /// storage adapter or participants are currently installed.
    /// </summary>
    public interface ICareerProfileService
    {
        string ProfileId { get; }

        string PlayerName { get; }

        string ActiveVehicleId { get; }

        CareerProfileData CurrentProfile { get; }

        bool TryCreateNewProfile(out string failure);

        bool TrySave(out string failure);

        bool TryLoad(out string failure);
    }

    /// <summary>
    /// Persistence adapter seam. JSON files, cloud saves, encrypted storage,
    /// and tests can provide this contract without changing CareerProfileSystem.
    /// </summary>
    public interface ICareerProfileStorage
    {
        bool TrySave(string profileId, string serializedProfile, out string failure);

        bool TryLoad(
            string profileId,
            out string serializedProfile,
            out string failure);
    }

    /// <summary>
    /// Global profile section seam. Wallets, bounty, garage ownership, and
    /// future reputation systems can participate without a monolithic save
    /// switch statement.
    /// </summary>
    public interface ICareerProfileParticipant
    {
        string ProfileSectionId { get; }

        void Capture(CareerProfileData profile);

        bool Restore(CareerProfileData profile, out string failure);
    }

    /// <summary>
    /// State seam for the active vehicle record. The profile system scopes the
    /// participant to one stable vehicle ID while performance and body systems
    /// own their own installed-ID sections.
    /// </summary>
    public interface ICareerVehicleStateParticipant
    {
        string VehicleStateSectionId { get; }

        void Capture(CareerVehicleData vehicle);

        bool Restore(CareerVehicleData vehicle, out string failure);
    }
}
