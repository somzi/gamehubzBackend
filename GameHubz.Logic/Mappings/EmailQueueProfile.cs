using GameHubz.DataModels.Api;

namespace GameHubz.Logic.Mappings
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
