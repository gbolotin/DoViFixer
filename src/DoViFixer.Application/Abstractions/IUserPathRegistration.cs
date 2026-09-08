namespace DoViFixer.Application.Abstractions;

public interface IUserPathRegistration
{
    bool AddDirectory(string directory, CancellationToken cancellationToken);
}
