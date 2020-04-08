using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;

namespace HappyTravel.Edo.Api.Models.Branches
{
    public readonly struct BranchInfo
    {
        [JsonConstructor]
        public BranchInfo(string title, int? id)
        {
            Title = title;
            Id = id;
        }


        /// <summary>
        ///     Title of the company branch.
        /// </summary>
        [Required]
        public string Title { get; }

        /// <summary>
        ///     Id of the company branch.
        /// </summary>
        public int? Id { get; }
    }
}