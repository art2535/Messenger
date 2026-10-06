using System.Security.Claims;
using Messenger.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Messenger.Tests.Helpers
{
    public static class ControllerTestHelper
    {
        public static void SetUser(ControllerBase controller, string externalId, string? name = null)
        {
            var claims = new List<Claim>
            {
                new Claim("sub", externalId),
                new Claim(ClaimTypes.NameIdentifier, externalId)
            };
            if (!string.IsNullOrEmpty(name))
                claims.Add(new Claim(ClaimTypes.Name, name));

            var identity = new ClaimsIdentity(claims, "TestAuth");
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(identity)
                }
            };
        }

        public static void SetAnonymous(ControllerBase controller)
        {
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity())
                }
            };
        }

        public static User CreateUser(Guid? userId = null, string externalId = "ext-1")
            => new User
            {
                UserId = userId ?? Guid.NewGuid(),
                ExternalId = externalId,
                FirstName = "Иван",
                LastName = "Тестов",
                Login = "ivan",
                RegistrationDate = DateOnly.FromDateTime(DateTime.UtcNow)
            };

        public static T? GetOkValue<T>(IActionResult result)
        {
            return result switch
            {
                OkObjectResult ok => ok.Value is T t ? t : default,
                ObjectResult obj when obj.StatusCode is null or >= 200 and < 300 => obj.Value is T t2 ? t2 : default,
                _ => default
            };
        }

        public static int? GetStatusCode(IActionResult result) => result switch
        {
            UnauthorizedResult => 401,
            UnauthorizedObjectResult u => u.StatusCode ?? 401,
            BadRequestObjectResult b => b.StatusCode ?? 400,
            NotFoundObjectResult n => n.StatusCode ?? 404,
            NotFoundResult => 404,
            StatusCodeResult s => s.StatusCode,
            ObjectResult o => o.StatusCode,
            _ => null
        };
    }
}
