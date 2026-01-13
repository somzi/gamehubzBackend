using System.Net;
using System.Text.Json;
using FluentValidation;
using Template.Api.Models;
using Template.Common.Consts;
using Template.Data.Exceptions;
using Template.Logic.Exceptions;
using Template.Logic.Exceptions.Base;
using Template.Logic.Services;

namespace Template.Api.Middleware
{
    public class ExceptionHandlingMiddlware
    {
        private readonly RequestDelegate next;
        private readonly LoggerService logger;

        public ExceptionHandlingMiddlware(RequestDelegate next, LoggerService logger)
        {
            this.next = next;
            this.logger = logger;
        }

        public async Task Invoke(HttpContext context)
        {
            try
            {
                context.Request.EnableBuffering();
                await this.next(context);
            }
            catch (CoreException ex)
            {
                await this.CreateExceptionResponse(context, HttpStatusCode.InternalServerError, ex, ExceptionCategory.Core);
            }
            catch (BaseUnauthorizedException ex)
            {
                await this.CreateExceptionResponse(context, HttpStatusCode.Unauthorized, ex, ExceptionCategory.Unauthorized);
            }
            catch (BaseDataException ex)
            {
                await this.CreateExceptionResponse(context, HttpStatusCode.BadRequest, ex, ExceptionCategory.Data);
            }
            catch (BaseException ex)
            {
                await this.CreateExceptionResponse(context, HttpStatusCode.BadRequest, ex, ExceptionCategory.Handled);
            }
            catch (ValidationException ex)
            {
                await this.CreateExceptionResponse(context, HttpStatusCode.BadRequest, ex, ExceptionCategory.Validation);
            }
            catch (System.UnauthorizedAccessException ex)
            {
                await this.CreateExceptionResponse(context, HttpStatusCode.Unauthorized, ex, ExceptionCategory.Unauthorized);
            }
            catch (Exception ex)
            {
                await this.CreateExceptionResponse(context, HttpStatusCode.InternalServerError, ex, ExceptionCategory.Unhandled);
            }
        }

        private async Task CreateExceptionResponse(HttpContext context, HttpStatusCode code, Exception exception, string category)
        {
            this.logger.LogError(context, exception, category);

            var result = CreateHandledErrorModel(exception);

            context.Response.ContentType = "application/json";
            context.Response.StatusCode = (int)code;
            await context.Response.WriteAsync(result);
        }

        private static string CreateHandledErrorModel(Exception exception)
        {
            var obj = new HandledErrorModel()
            {
                Message = exception.Message,
                Details = exception.ToString() ?? ""
            };

            if (exception is ValidationException validationException)
            {
                obj.Items = validationException.Errors.Select(x => new ValidationErrorItem()
                {
                    Property = $"{char.ToLower(x.PropertyName[0])}{x.PropertyName[1..]}",
                    Message = x.ErrorMessage
                }).ToList();
            }

            string result = JsonSerializer.Serialize(obj);

            return result;
        }
    }
}