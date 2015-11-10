using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        private const string IsDependencyAttributeName = "IsDependency";

        public const string ViewDataKey = "NamedScriptInfos";

        static readonly Regex _namePatern = new Regex(@"^[^\s|;,]+$");
        static readonly Regex _listPatern = new Regex(@"^([^\s|;,]+[\s|;,]+)*[^\s|;,]+$");

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
            var hasName = null != Name && _namePatern.IsMatch(Name);
            var hasSrc = !string.IsNullOrWhiteSpace(Src);
            if (!hasName && !hasSrc)
                throw new ArgumentException("Name is required.  It must be a single string without whitespace, commas, pipes or semi-colons.", nameof(Name));
            var namedScript = new NamedScriptInfo { Name = Name ?? Src, Src = Src, Dependancies = _dependsOn, Aliases = _aliases };
            if (hasSrc)
            {
                if (!Src.EndsWith(".min.js"))
                {
                    //TODO:  Consider automatically looking at a minified source cache
                }
            }
            else
            {
                //Get the script contents

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
}