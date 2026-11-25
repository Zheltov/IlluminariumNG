namespace IlluminariumNG.Application;

internal interface IProjectFile
{
    Task<LightingProject> ReadAsync(Stream stream);
    Task SaveAsync(string path, LightingProject project);
}
