namespace RplaceModtools;

public interface ITransientStateInfo
{
    public TimeSpan PersistsFor { get; set; }
    public DateTime SpawnedOn { get; set; }
}