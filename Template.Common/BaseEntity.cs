using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Template.Common.Extensions;

namespace Template.Common
{
    public abstract class BaseEntity
    {
        [Key]
        public Guid? Id { get; set; }

        public DateTime? CreatedOn { get; set; }

        public DateTime? ModifiedOn { get; set; }

        [Required]
        public bool IsDeleted { get; set; }

        public Guid? CreatedBy { get; set; }

        public Guid? ModifiedBy { get; set; }

        [NotMapped]
        public bool IsNew
        {
            get
            {
                return this.Id.IsNullOrEmpty();
            }
        }
    }
}