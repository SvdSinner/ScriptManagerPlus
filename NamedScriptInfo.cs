using System.Collections.Generic;
using System.Linq;

namespace ScriptManagerPlus
{
    public class NamedScriptInfo
    {
        /// <summary>
        /// Gets or sets the unique name for deduplication.
        /// </summary>
        /// <value>
        /// The unique name.
        /// </value>
        public string Name { get; set; }
        /// <summary>
        /// Gets or sets the script.
        /// </summary>
        /// <value>
        /// The script.
        /// </value>
        public string Script { get; set; }
        /// <summary>
        /// Gets or sets the source url for the script.
        /// </summary>
        /// <value>
        /// The source url.
        /// </value>
        public string Src { get; set; }
        /// <summary>
        /// Gets or sets the script's dependencies.
        /// </summary>
        /// <value>
        /// One or more names of scripts that the script depends on.  The list may be delimited by space, comma, semicolon or tab.
        /// </value>
        public string[] Dependencies { get; set; }
        /// <summary>
        /// Gets or sets aliases for the script.
        /// </summary>
        /// <value>
        /// One or more aliases.    The list may be delimited by space, comma, semicolon or tab.
        /// </value>
        /// <remarks>Aliases are used to allow a script to be depended on by multiple names should be unique.  </remarks>
        /// <example><script src="SomeBigUrlToTheScript" script-alias="ShortName"></script></example>
        public string[] Aliases { get; set; }
        private string[] _allNames;
        /// <summary>
        /// Gets or sets a value indicating whether this instance is dependency and should not be rendered unless another script depends on it.
        /// </summary>
        /// <value>
        ///   <c>true</c> if this instance should not render if no other scripts reference it in their Script-Depends-On attribute; otherwise, <c>false</c>.
        /// </value>
        public bool IsDependency { get; set; }
        /// <summary>
        /// Gets name plus all aliases.  Used by the script-depends-on tag for dependency handling.
        /// </summary>
        /// <returns>string[] of all referencable names</returns>
        public string[] GetAllNames()
        {
            if (null == _allNames)
            {
                var names = new List<string>();
                if (null != Aliases) names.AddRange(Aliases);
                if (!string.IsNullOrWhiteSpace(Name)) names.Add(Name);
                if (!string.IsNullOrWhiteSpace(Src)) names.Add(Src);
                _allNames = names.Distinct().ToArray();
            }
            return _allNames;
        }
    }
}