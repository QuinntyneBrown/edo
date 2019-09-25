using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Linq.Expressions;
using CSharpFunctionalExtensions;

namespace HappyTravel.Edo.Api.Services.Markups.Templates
{
    public class MarkupPolicyTemplateService : IMarkupPolicyTemplateService
    {
        public IReadOnlyCollection<MarkupPolicyTemplate> Get()
        {
            return new ReadOnlyCollection<MarkupPolicyTemplate>(Templates);
        }

        public Result<Expression<Func<decimal, decimal>>> CreateExpression(int templateId, IDictionary<string, decimal> settings)
        {
            var template = Templates.SingleOrDefault(t => t.Id == templateId);
            if(template == default)
                return Result.Fail<Expression<Func<decimal, decimal>>>($"Could not find template by id {templateId}");
            
            if(!template.SettingsValidator(settings))
                return Result.Fail<Expression<Func<decimal, decimal>>>("Invalid template settings");
            
            if(!template.IsEnabled)
                return Result.Fail<Expression<Func<decimal, decimal>>>("Could not create expression for disabled template");
            
            return Result.Ok(template.ExpressionFactory(settings));
        }
        
        // !! These templates are referenced from MarkupPolicies and should not be changed without appropriate migration.
        private static readonly MarkupPolicyTemplate[] Templates = 
        {
            new MarkupPolicyTemplate
            {
                Id = 1,
                Title = "Multiplier",
                IsEnabled = true,
                ExpressionFactory = settings => rawValue => rawValue * settings["Factor"],
                SettingsValidator = settings => settings.Keys.Count == 1 && settings["Factor"] > 1
            },
            new MarkupPolicyTemplate
            {
                Id = 2,
                Title = "Addition",
                IsEnabled = true,
                ExpressionFactory = settings => rawValue => rawValue + settings["Addition"],
                SettingsValidator = settings => settings.Keys.Count == 1 && settings["Addition"] > 0
            },
        };
    }
}