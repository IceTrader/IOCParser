using Core.Models;
using System.Text.RegularExpressions;

namespace Core.Interfaces;

public interface IIndicatorExtractor
{
    IEnumerable<Indicator> Extract(string text, ScanOptions? options = null);
}