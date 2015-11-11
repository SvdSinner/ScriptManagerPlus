using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNet.Http;
using Microsoft.AspNet.Razor.Runtime.TagHelpers;

namespace ScriptManagerPlus
{
    /// <summary>
    /// Tag Helper for ordering, deduping and rendering script tags with the script-name attribute.
    /// </summary>
    [HtmlTargetElement("script", Attributes = RenderAttributeName)]
    public class InlineScriptTagHelper : TagHelper
    {
        private static readonly Regex _inScriptTagsPattern =
            new Regex(@"\<\s*script[^\>]+((?<=/)\>|\>([^\<]|\<(?!/?\s*script))*\</\s*script\s*\>)$",
                RegexOptions.Compiled | RegexOptions.CultureInvariant |
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
        private const string RenderAttributeName = "script-render";
        private readonly IHttpContextAccessor _httpContextAccessor;

        private enum RenderOptions
        {
            Basic = 0,
            RequireDependencies = 1,
            SkipProblems = 2
        }

        private RenderOptions _options = RenderOptions.Basic;

        public InlineScriptTagHelper(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        /// <summary>
        /// Gets or sets the dependency validation methodology.
        /// Basic simply enforces that a script are run after any scripts that depend on them
        /// </summary>
        /// <value>
        /// The dependency validation.
        /// </value>
        [HtmlAttributeName(RenderAttributeName)]
        public string DependencyValidation
        {
            get { return Enum.GetName(typeof(RenderOptions), _options); }
            set
            {
                if (Enum.GetNames(typeof(RenderOptions)).Contains(value, StringComparer.OrdinalIgnoreCase))
                    _options = (RenderOptions)Enum.Parse(typeof(RenderOptions), value, true);
                else _options = RenderOptions.Basic;
            }
        }

        public override void Process(TagHelperContext context, TagHelperOutput output)
        {
            //if no scripts were added, suppress the contents
            if (!_httpContextAccessor.HttpContext.Items.ContainsKey(InlineScriptConcatenatorTagHelper.ViewDataKey))
            {
                output.SuppressOutput();
                return;
            }

            //Otherwise get all the scripts for the page
            var scripts =
                _httpContextAccessor.HttpContext.Items[InlineScriptConcatenatorTagHelper.ViewDataKey] as
                    IDictionary<string, NamedScriptInfo>;
            if (null == scripts)
            {
                output.SuppressOutput();
                return;
            }

            //Concatenate all of them and set them as the contents of this tag
            var allScripts = string.Join("\r\n", OrderedScripts(scripts.Values).Select(os => os.Script));
            output.TagMode = TagMode.StartTagOnly;
            //HACK:Need to figure out how to get rid of the script tags for the placeholder element
            allScripts = $"</script>\r\n{allScripts}";//HACK:ugly
            var unminifiedContent = output.Content.SetContentEncoded(allScripts);
            Debug.WriteLine(unminifiedContent.GetContent());
            //TODO:Impliment dynamic minification (Assuming that some scenarios will be sped up, and others slowed down.  Leave choice to user)
        }

        private IEnumerable<NamedScriptInfo> OrderedScripts(IEnumerable<NamedScriptInfo> scripts)
        {
            Contract.Requires(null != scripts);
            var orderedScripts = scripts.ToList();

            //HACK:  No effort put into optimizing for large lists or complex dependencies beyond limiting passes if a recursive situation arrises.
            var ordered = false;
            var maxPasses = 15;
            var issues = false;
            while (!ordered && maxPasses-- > 0)
                switch (_options)
                {
                    case RenderOptions.RequireDependencies:
                    case RenderOptions.SkipProblems:
                        //Both of these methods look forward for any of a script's own dependencies and moves them before the dependent script.
                        var satisfiedDependencies = new List<string>(orderedScripts.Count);
                        for (var i = 0; i < orderedScripts.Count; i++)
                        {
                            issues = false;
                            if (null != orderedScripts[i].Dependencies)
                                foreach (
                                    var dependency in
                                        orderedScripts[i].Dependencies.Where(d => !satisfiedDependencies.Contains(d))
                                            .ToArray())
                                {
                                    issues = true;
                                    //Unsatisfied Dependency Search
                                    var tmp =
                                        orderedScripts.Skip(i+1).FirstOrDefault(s => s.GetAllNames().Contains(dependency));
                                    if (tmp != null)
                                    {
                                        orderedScripts.Remove(tmp);
                                        orderedScripts.Insert(i, tmp);
                                    }
                                    else 
                                    {
                                        var msg =
                                            $"Dependency missing on {orderedScripts[i].Name}.  Missing dependency is \"{dependency}\"";
                                        if (_options == RenderOptions.RequireDependencies)
                                            throw new DependacyMissingException(msg);
                                        Debug.WriteLine($"{msg}.  Script will be discarded.");
                                        orderedScripts.RemoveAt(i--);
                                    }
                                }
                            satisfiedDependencies.AddRange(orderedScripts[i].GetAllNames());
                        }
                        ordered = !issues;
                        break;
                    //Basic simply looks for scripts before itself that depends on it, and moves them after themselves.
                    case RenderOptions.Basic:
                        issues = false;
                        for (var i = 1; i < orderedScripts.Count; i++)
                        {
                            var current = orderedScripts[i];
                            var dependentScript =
                                orderedScripts.Take(i)
                                    .FirstOrDefault(
                                        ds =>
                                            null != ds.Dependencies &&
                                            ds.Dependencies.Intersect(current.GetAllNames()).Any());
                            if (null == dependentScript) continue;
                            issues = true;
                            if (orderedScripts.Remove(dependentScript))
                                orderedScripts.Insert(i, dependentScript);
                        }
                        ordered = !issues;
                        break;
                }
            foreach (var script in orderedScripts.Where(s => !_inScriptTagsPattern.IsMatch(s.Script??"")))
            {
                var withTags = new StringBuilder("<script type='text/javascript' ");
                if (string.IsNullOrWhiteSpace(script.Src))
                {
                    withTags.AppendLine(">");
                    withTags.AppendLine(script.Script);
                }
                else
                {
                    var tildaPos = script.Src.LastIndexOf('~');
                    if (tildaPos>=0)
                    {
                        //TODO:Parse the ~ into the proper Url
                        var pathEnd = tildaPos == script.Src.Length - 1 ? "" : script.Src.Substring(tildaPos + 1);
                        var request = _httpContextAccessor.HttpContext.Request;
                        var baseUrl = request.Scheme + "://" + request.Host + "/" 
                                      + (request.PathBase.HasValue ? request.PathBase.Value : "");
                        script.Src = baseUrl.TrimEnd('/') + pathEnd;
                    }
                    withTags.Append($"src='{script.Src}' >");
                }
                withTags.Append("</script>");
                script.Script = withTags.ToString();
                Debug.Assert(_inScriptTagsPattern.IsMatch(script.Script));
            }
            return orderedScripts;
        }

    }
}