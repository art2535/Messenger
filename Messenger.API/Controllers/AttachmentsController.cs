using Asp.Versioning;
using Messenger.Core.DTOs.Attachments;
using Messenger.Core.Models;
using Messenger.Core.Responses;
using Messenger.Infrastructure.Interfaces.Attachments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel;

namespace Messenger.API.Controllers
{
    /// <summary>
    /// Контроллер для управления вложениями.
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("api/v{version:apiVersion}/attachments")]
    [ApiVersion("1.0")]
    [Produces("application/json")]
    [Consumes("application/json")]
    [Tags("Attachments")]
    public class AttachmentsController : ControllerBase
    {
        private readonly IAttachmentWorkflowService _attachmentWorkflowService;
        private readonly ILogger<AttachmentsController> _logger;

        public AttachmentsController(
            IAttachmentWorkflowService attachmentWorkflowService,
            ILogger<AttachmentsController> logger)
        {
            _attachmentWorkflowService = attachmentWorkflowService;
            _logger = logger;
        }

        /// <summary>
        /// Получение списка вложений по идентификатору сообщения.
        /// </summary>
        [HttpGet("{messageId:guid}")]
        [EndpointName("GetAttachmentsByMessage")]
        [EndpointSummary("Получение списка вложений по идентификатору сообщения")]
        [EndpointDescription("Возвращает все файлы (вложения), прикреплённые к указанному сообщению. Требуется действительный JWT-токен.")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetAttachmentsByMessageAsync(
            [FromRoute, Description("Уникальный идентификатор сообщения (GUID)")] Guid messageId,
            CancellationToken ct = default)
        {
            try
            {
                var attachments = await _attachmentWorkflowService
                    .GetAttachmentsByMessageIdAsync(messageId, ct);

                return Ok(ApiResponse.Success(attachments));
            }
            catch (InvalidOperationException ex)
            {
                return NotFound(ApiResponse.Failure(ex.Message, "NOT_FOUND"));
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Ошибка при получении вложений для сообщения {MessageId}",
                    messageId);

                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }

        /// <summary>
        /// Добавление нового вложения к сообщению.
        /// </summary>
        [HttpPost("{messageId:guid}")]
        [EndpointName("CreateAttachment")]
        [EndpointSummary("Добавление нового вложения к сообщению")]
        [EndpointDescription("Создаёт запись о новом файле (вложении), прикреплённом к сообщению. " +
            "Требуется передать данные файла в теле запроса. Идентификатор вложения генерируется автоматически. " +
            "Требуется действительный JWT-токен.")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> PostCreateAttachmentAsync(
            [FromRoute, Description("Уникальный идентификатор сообщения (GUID), к которому добавляется вложение")] Guid messageId,
            [FromBody, Description("Данные нового вложения")] CreateAttachmentRequest request,
            CancellationToken ct = default)
        {
            try
            {
                var attachment = new Attachment
                {
                    AttachmentId = Guid.NewGuid(),
                    MessageId = messageId,
                    FileName = request.FileName,
                    FileType = request.FileType,
                    SizeInBytes = request.SizeInBytes,
                    Url = request.Url
                };

                await _attachmentWorkflowService.AddAttachmentAsync(attachment, ct);

                return Ok(ApiResponse.Success(message: "Вложение успешно добавлено"));
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ApiResponse.Failure(ex.Message, "INVALID_REQUEST"));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ApiResponse.Failure(ex.Message, "INVALID_REQUEST"));
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Ошибка при добавлении вложения к сообщению {MessageId}",
                    messageId);

                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    ApiResponse.Failure(ex.Message, "INTERNAL_ERROR"));
            }
        }
    }
}
