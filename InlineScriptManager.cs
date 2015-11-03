using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNet.Http;
using Microsoft.AspNet.Razor.Runtime.TagHelpers;

namespace ScriptManagerPlus
{
    /// <summary>
    /// Tag Helper for view scripts that should be ordered, deduped and rendered at the script tag with the script-render attribute.
    /// </summary>
    [HtmlTargetElement("script", Attributes = AddAttributeName)]
    [HtmlTargetElement("script", Attributes = DependsOnAttributeName)]
    [HtmlTargetElement("script", Attributes = AliasAttributeName)]
    [HtmlTargetElement("script", Attributes = IsDependencyAttributeName)]
    public class InlineScriptConcatenatorTagHelper : TagHelper
    {
        private const string AddAttributeName = "script-name";
        private const string DependsOnAttributeName = "script-depends-on";
        private const string AliasAttributeName = "script-alias";
        private const string SrcAttributeName = "src";
        private const string IsDependencyAttributeName = "IsD  ependency";

        public const string ViewDataKey = "NamedScriptInfos";

        static readonly Regex _namePatern = new Regex(@"^[^\s|;,]+$");
        static readonly Regex _listPatern = new Regex(@"^[^\s|;,]+([\s|;,]+)*[^\s|;,]+$");

        private readonly IHttpContextAccessor _httpContextAccessor;
        private string[] _aliases;
        private string[] _dependsOn;

        public InlineScriptConcatenatorTagHelper(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        /// <summary>
        /// Gets or sets if this script should be omitted if no other scripts depend on it.
        /// </summary>
        /// <value>
        ///   <c>true</c> if this instance is not needed if not depended on; otherwise, <c>false</c> will cause it to always render.
        /// </value>
        [HtmlAttributeName(IsDependencyAttributeName)]
        public bool IsDependency { get; set; }

        /// <summary>
        /// Gets or sets the script name.
        /// </summary>
        /// <value>
        /// The unique name for de-duplication.
        /// </value>
        [HtmlAttributeName(AddAttributeName)]
        public string Name { get; set; }
        /// <summary>
        /// Gets or sets a list of scripts that must be loaded before execution.  List can be delimited by spaces, commas, pipes or semi-colons.
        /// </summary>
        /// <value>
        /// The name or aliases of all dependant on scripts
        /// </value>
        [HtmlAttributeName(DependsOnAttributeName)]
        public string DependsOn
        {
            get { return null == _dependsOn ? "" : string.Join(" ", _dependsOn); }
            set
            {
                if (_listPatern.IsMatch(value))
                    _dependsOn = value.Split(" \r\n\t,|;".ToCharArray(), StringSplitOptions.RemoveEmptyEntries);
                else throw new ArgumentOutOfRangeException(nameof(DependsOn), "Invalid format");
            }
        }

        /// <summary>
        /// Gets or sets a list of aliases this script can be referenced by.  List can be delimited by spaces, commas, pipes or semi-colons.
        /// </summary>
        /// <value>
        /// The aliases
        /// </value>
        [HtmlAttributeName(AliasAttributeName)]
        public string Aliases
        {
            get { return null == _aliases ? "" : string.Join(" ", _aliases); }
            set
            {
                if (_listPatern.IsMatch(value))
                    _aliases = value.Split(" \r\n\t,|;".ToCharArray(), StringSplitOptions.RemoveEmptyEntries);
                else throw new ArgumentOutOfRangeException(nameof(Aliases), "Invalid format");
            }
        }
        /// <summary>
        /// Address of the external script to use.
        /// </summary>
        /// <remarks>
        /// Passed through to the generated HTML in all cases.
        /// </remarks>
        [HtmlAttributeName(SrcAttributeName)]
        public string Src { get; set; }

        /// <summary>
        /// Asynchronously removes the script from the render pipeline and stores it into the HTML context to be rendered later.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="output">The output.</param>
        /// <returns></returns>
        public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
        {
            //Validate inputs
            var hasName = _namePatern.IsMatch(Name);
            var hasSrc = !string.IsNullOrWhiteSpace(Src);
            if (!hasName && !hasSrc)
                throw new ArgumentException("Name is required.  It must be a single string without whitespace, commas, pipes or semi-colons.", nameof(Name));
            var namedScript = new NamedScriptInfo { Name = Name ?? Src, Src = Src, Dependancies = _dependsOn, Aliases = _aliases };
            if (!hasSrc)
            {
                //Get the script contents
                if (!Src.EndsWith(".min.js"))
                {
                    //TODO:  Consider automatically looking at a minified source cache
                }
                var contents = await context.GetChildContentAsync();
                var scriptContent = contents.GetContent();
                namedScript.Script = scriptContent;
            }

            //Save them into the http Context
            if (_httpContextAccessor.HttpContext.Items.ContainsKey(ViewDataKey))
            {
                var scripts = (IDictionary<string, NamedScriptInfo>)_httpContextAccessor.HttpContext.Items[ViewDataKey];
                if (scripts.ContainsKey(namedScript.Name))
                    Debug.WriteLine("Duplicate script ignored");
                else
                    scripts.Add(namedScript.Name, namedScript);
            }
            else
                _httpContextAccessor.HttpContext.Items[ViewDataKey] = new Dictionary<string, NamedScriptInfo> { { namedScript.Name, namedScript } };

            //suppress any output
            output.SuppressOutput();
        }
    }

    /// <summary>
    /// Tag Helper for ordering, deduping and rendering script tags with the script-name attribute.
    /// </summary>
    [HtmlTargetElement("script", Attributes = RenderAttributeName)]
    public class InlineScriptTagHelper : TagHelper
    {
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
        /// Gets or sets the dependancy validation methodology.
        /// Basic simply enforces that a script are run after any scripts that depend on them
        /// </summary>
        /// <value>
        /// The dependancy validation.
        /// </value>
        [HtmlAttributeName(RenderAttributeName)]
        public string DependancyValidation
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
            var unminifiedContent = output.Content.SetContentEncoded(string.Join("\r\n", OrderedScripts(scripts.Values).Select(os => os.Script)));

            //TODO:Impliment dynamic minification (Assuming that some scenarios will be sped up, and others slowed down.  Leave choice to user)
        }

        private IEnumerable<NamedScriptInfo> OrderedScripts(IEnumerable<NamedScriptInfo> scripts)
        {
            Contract.Requires(null != scripts);
            var orderedScripts = scripts.ToList();

            //HACK:  No effort put into optimizing for large lists or complex dependancies beyond limiting passes if a recursive situation arrises.
            var ordered = false;
            var maxPasses = 15;
            var issues = false;
            while (!ordered && maxPasses-- > 0)
                switch (_options)
                {
                    case RenderOptions.RequireDependencies:
                    case RenderOptions.SkipProblems:
                        //Both of these methods look forward for any of a script's own dependancies and moves them before the dependant script.
                        var satisfiedDependancies = new List<string>(orderedScripts.Count);
                        for (var i = 0; i < orderedScripts.Count; i++)
                        {
                            issues = false;
                            if (null != orderedScripts[i].Dependancies)
                                foreach (
                                    var dependancy in
                                        orderedScripts[i].Dependancies.Where(d => !satisfiedDependancies.Contains(d))
                                            .ToArray())
                                {
                                    var tmp =
                                        orderedScripts.Skip(i).FirstOrDefault(s => s.GetAllNames().Contains(dependancy));
                                    if (tmp != null)
                                    {
                                        orderedScripts.Remove(tmp);
                                        orderedScripts.Insert(i--, tmp);
                                    }
                                    else
                                    {
                                        var msg =
                                            $"Dependancy missing on {orderedScripts[i].Name}.  Missing dependancy is \"{dependancy}\"";
                                        if (_options == RenderOptions.RequireDependencies)
                                            throw new DependacyMissingException(msg);
                                        Debug.WriteLine($"{msg}.  Script will be discarded.");
                                        orderedScripts.RemoveAt(i--);
                                        issues = true;
                                    }
                                }
                            satisfiedDependancies.AddRange(orderedScripts[i].GetAllNames());
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
                                            null != ds.Dependancies &&
                                            ds.Dependancies.Intersect(current.GetAllNames()).Any());
                            if (null == dependentScript) continue;
                            issues = true;
                            if (orderedScripts.Remove(dependentScript))
                                orderedScripts.Insert(i, dependentScript);
                        }
                        ordered = !issues;
                        break;
                }
            return orderedScripts;
        }

    }

    public class DependacyMissingException : Exception
    {
        public DependacyMissingException(string message) : base(message)
        { }
    }
}

