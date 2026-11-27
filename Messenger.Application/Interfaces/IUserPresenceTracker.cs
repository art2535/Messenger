namespace Messenger.Application.Interfaces;

/// <summary>Отслеживает активные SignalR-подключения пользователей.</summary>
public interface IUserPresenceTracker
{
    /// <summary>Регистрирует подключение и возвращает true при переходе Offline → Online.</summary>
    Task<bool> ConnectAsync(Guid userId, string connectionId, CancellationToken cancellationToken = default);

    /// <summary>Удаляет подключение и возвращает true при переходе Online → Offline.</summary>
    Task<bool> DisconnectAsync(Guid userId, string connectionId, CancellationToken cancellationToken = default);

    /// <summary>Проверяет наличие хотя бы одного активного SignalR-подключения.</summary>
    Task<bool> HasActiveConnectionsAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Возвращает все активные connection ID пользователя из общего presence-хранилища.</summary>
    Task<IReadOnlyList<string>> GetActiveConnectionIdsAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Продлевает lease всех подключений текущего экземпляра приложения.</summary>
    Task HeartbeatAsync(CancellationToken cancellationToken = default);
}