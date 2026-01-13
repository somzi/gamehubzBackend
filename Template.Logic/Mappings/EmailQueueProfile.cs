using Template.DataModels.Api;

namespace Template.Logic.Mappings
{
    public class EmailQueueProfile : Profile
    {
        public EmailQueueProfile()
        {
            this.CreateMap<EmailQueueEntity, EmailQueueModel>().ReverseMap();

            this.CreateMap<EmailQueueEntity, EmailModel>();
        }
    }
}