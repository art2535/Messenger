using Asp.Versioning;
using Messenger.API.Responses;
using Messenger.Core.DTOs.Attachments;
using Messenger.Core.Interfaces;
using Messenger.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel;

namespace Messenger.API.Controllers
{
    /// <summary>
    /// Контроллер для управления вложениями
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
        private readonly IAttachmentService _attachmentService;

        public AttachmentsController(IAttachmentService attachmentService)
        {
            _attachmentService = attachmentService;
        }

        /// <summary>
        /// Получение списка вложений по идентификатору сообщения
        /// </summary>
        [HttpGet("{messageId}")]
        [EndpointName("GetAttachmentsByMessage")]
        [EndpointSummary("Получение списка вложений по идентификатору сообщения")]
        [EndpointDescription("Возвращает все файлы (вложения), прикреплённые к указанному сообщению. Требуется действительный JWT-токен.")]
        [ProducesResponseType(typeof(GetAttachmentsSuccessResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetAttachmentsByMessageAsync(
            [Description("Уникальный идентификатор сообщения (GUID)")] Guid messageId,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var attachents = await _attachmentService.GetAttachmentsByMessageIdAsync(messageId, cancellationToken);

                return Ok(new GetAttachmentsSuccessResponse
                {
                    IsSuccess = true,
                    Data = attachents
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new ErrorResponse
                {
                    IsSuccess = false,
                    Error = ex.Message
                });
            }
        }

        /// <summary>
        /// Добавление нового вложения к сообщению
        /// </summary>
        [HttpPost("{messageId}")]
        [EndpointName("CreateAttachment")]
        [EndpointSummary("Добавление нового вложения к сообщению")]
        [EndpointDescription("Создаёт запись о новом файле (вложении), прикреплённом к сообщению. " +
            "Требуется передать данные файла в теле запроса. Идентификатор вложения генерируется автоматически. " +
            "Требуется действительный JWT-токен.")]
        [ProducesResponseType(typeof(CreateAttachmentSuccessResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> CreateAttachmentAsync(
            [Description("Уникальный идентификатор сообщения (GUID), к которому добавляется вложение")] Guid messageId,
            [FromBody, Description("Данные нового вложения")] CreateAttachmentRequest request,
            CancellationToken cancellationToken = default)
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

                await _attachmentService.AddAttachmentAsync(attachment, cancellationToken);

                return Ok(new CreateAttachmentSuccessResponse
                {
                    IsSuccess = true,
                    Message = "Вложение успешно добавлено"
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new ErrorResponse
                {
                    IsSuccess = false,
                    Error = ex.Message
                });
            }
        }
    }
}
