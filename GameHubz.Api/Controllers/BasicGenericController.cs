using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using GameHubz.Common;
using GameHubz.Common.Consts;
using GameHubz.Common.Models;
using GameHubz.DataModels.Interfaces;
using GameHubz.DataModels.Models;
using GameHubz.Logic.Services;

namespace GameHubz.Api.Controllers
{
    public abstract class BasicGenericController<TService, TEntity, TDto, TDtoPost, TDtoEdit> : ControllerBase
        where TService : AppBaseServiceGeneric<TEntity, TDto, TDtoPost, TDtoEdit>
        where TEntity : BaseEntity, new()
        where TDtoPost : IEditableDto
    {
        public BasicGenericController(
            TService service,
            AppAuthorizationService appAuthorizationService)
        {
            this.Service = service;
            this.AppAuthorizationService = appAuthorizationService;
        }

        protected TService Service { get; private set; }

        protected AppAuthorizationService AppAuthorizationService { get; private set; }

        [HttpDelete("{id}")]
        public virtual async Task Delete(
            [FromRoute, Required] Guid id)
        {
            await this.AppAuthorizationService.CheckAuthorization(this.UserRolesDelete());

            await this.Service.DeleteEntity(id);

            this.Ok();
        }

        [HttpGet("{id}")]
        public virtual async Task<TDto> GetById(
            [FromRoute, Required] Guid id)
        {
            await this.AppAuthorizationService.CheckAuthorization(this.UserRolesRead());

            TDto model = await this.Service.GetEntityById(id);

            return model;
        }

        [HttpGet("")]
        public virtual async Task<EntityListDto<TDto>> GetList(
            [FromQuery] int? pageIndex,
            [FromQuery] int? pageSize,
            [FromQuery] List<SortItem> sortItems,
            [FromQuery] List<FilterItem> filterItems)
        {
            //await this.AppAuthorizationService.CheckAuthorization(this.UserRolesRead());

            EntityListDto<TDto> list = await this.Service.GetEntities(
                filterItems,
                sortItems,
                pageIndex,
                pageSize);

            return list;
        }

        [HttpPost("")]
        public virtual async Task<TDto> SaveEntity([FromBody] TDtoPost modelSave)
        {
            await this.AppAuthorizationService.CheckAuthorization(this.UserRolesSave());

            TDto model = await this.Service.SaveEntity(modelSave);
            return model;
        }

        protected virtual UserRoleEnum[]? UserRolesRead()
        {
            return null;
        }

        protected virtual UserRoleEnum[]? UserRolesSave()
        {
            return null;
        }

        protected virtual UserRoleEnum[]? UserRolesDelete()
        {
            return null;
        }
    }
}