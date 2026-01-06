using System;

namespace WebUI.Models
{
    public class ProjectUiModel
    {
        public Guid Id { get; set; }
        public string ProjectName { get; set; } = "";
        public string CreatedBy { get; set; } = "";
        public DateTime Created { get; set; }
        public string Device { get; set; } = "";
        public string FileType { get; set; } = "";
        public string Description { get; set; } = "";
        public bool Status { get; set; } = true;
    }
}
