using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Messenger.Core.Models;

[Table("Logs", Schema = "logging")]
public class Log
{
    [Key]
    [Column("Id")]
    public Guid Id { get; set; }

    [Column("Message")]
    public string? Message { get; set; }

    [Column("MessageTemplate")]
    public string? MessageTemplate { get; set; }

    [Column("Level")]
    [StringLength(32)]
    public string? Level { get; set; }

    [Column("Timestamp")]
    public DateTime Timestamp { get; set; }

    [Column("Exception")]
    public string? Exception { get; set; }

    [Column("LogEvent", TypeName = "jsonb")]
    public string? LogEvent { get; set; }

    [Column("Application")]
    [StringLength(128)]
    public string? Application { get; set; }

    [Column("MachineName")]
    [StringLength(128)]
    public string? MachineName { get; set; }

    [Column("Environment")]
    [StringLength(64)]
    public string? Environment { get; set; }
}
