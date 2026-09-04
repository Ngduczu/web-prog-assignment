using LMS.Api.application.abstractions.auth;
using Microsoft.AspNetCore.Mvc;

namespace LMS.Api.presentation.controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    
    
    public IActionResult Login()
    {
        return Ok();
    }
    
    [HttpPost("oauth")]
    public IActionResult LoginWithExternalProvider([FromQuery]string provider)
    {
        
        
        return Ok();
    }
}