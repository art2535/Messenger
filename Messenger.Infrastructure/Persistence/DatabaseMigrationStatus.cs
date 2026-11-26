namespace Messenger.Infrastructure.Persistence
{
    public sealed class DatabaseMigrationStatus
    {
        private volatile MigrationState _state = MigrationState.Pending;
        private Exception? _exception;

        public MigrationState State => _state;

        public Exception? Exception => _exception;

        public void MarkRunning()
        {
            _exception = null;
            _state = MigrationState.Running;
        }

        public void MarkCompleted()
        {
            _exception = null;
            _state = MigrationState.Completed;
        }

        public void MarkFailed(Exception exception)
        {
            _exception = exception;
            _state = MigrationState.Failed;
        }
    }

    public enum MigrationState
    {
        Pending,
        Running,
        Completed,
        Failed
    }
}
