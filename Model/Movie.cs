using Azure.Search.Documents.Indexes.Models;
using Azure.Search.Documents.Indexes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CosmosMovieGuide
{

    public class Movie
    {

        public string id { get; set; }
        public string name { get; set; }
        public string synopsis { get; set; }

        public bool embeddingStatus { get; set; }
    }     


}
