namespace ReciteHelper.Core.Interfaces.Services;

public interface IGalGameCreationService
{
    Task CreateAsync(string projectPath, string deepSeekKey);
}
