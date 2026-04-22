using Microsoft.AspNetCore.Mvc;

namespace MeetingRoomBooking.Controllers
{
    
    
        [ApiController]
        [Route("api/test-email")]
        public class TestEmailController : ControllerBase
        {
            private readonly EmailService _emailService;

            public TestEmailController(EmailService emailService)
            {
                _emailService = emailService;
            }

            [HttpGet]
            public async Task<IActionResult> Send()
            {
                await _emailService.SendAsync(
                    "email-cua-ban@gmail.com",
                    "Test Email",
                    "<h2>Test thành công 🎉</h2>"
                );

                return Ok("Sent!");
            }
        }
    }

