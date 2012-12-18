﻿using System.Collections.Generic;

using SKBKontur.Catalogue.Core.Web.Blocks.Button;
using SKBKontur.Catalogue.Core.Web.Models.HtmlModels;

namespace SKBKontur.Catalogue.RemoteTaskQueue.TaskMonitoringViewer.Models.Html
{
    public class SearchPanelHtmlModel
    {
        public KeyValuePair<TextBoxHtmlModel, CheckBoxHtmlModel>[] States { get; set; }
        public SelectBoxHtmlModel TaskName { get; set; }
        public ButtonHtmlModel SearchButton { get; set; }
    }
}