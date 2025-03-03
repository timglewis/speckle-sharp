using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Speckle.Core.Kits;
using Speckle.Core.Logging;

namespace ConnectorRevit.Revit
{
  public class ErrorEater : IFailuresPreprocessor
  {
    private ISpeckleConverter _converter;
    private List<Exception> _exceptions = new();
    public Dictionary<string, int> CommitErrorsDict = new();

    public ErrorEater(ISpeckleConverter converter)
    {
      _converter = converter;
    }

    public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
    {
      var resolvedFailures = 0;
      var failedElements = new List<ElementId>();
      // Inside event handler, get all warnings
      var failList = failuresAccessor.GetFailureMessages();
      foreach (FailureMessageAccessor failure in failList)
      {
        // check FailureDefinitionIds against ones that you want to dismiss,
        //FailureDefinitionId failID = failure.GetFailureDefinitionId();
        // prevent Revit from showing Unenclosed room warnings
        //if (failID == BuiltInFailures.RoomFailures.RoomNotEnclosed)
        //{
        var t = failure.GetDescriptionText();

        var s = failure.GetSeverity();
        if (s == FailureSeverity.Warning)
        {
          // just delete the warnings for now
          failuresAccessor.DeleteWarning(failure);
          resolvedFailures++;
          continue;
        }

        try
        {
          failuresAccessor.ResolveFailure(failure);
          resolvedFailures++;
        }
        catch
        {
          var idsToDelete = failure.GetFailingElementIds().ToList();

          if (failuresAccessor.IsElementsDeletionPermitted(idsToDelete))
          {
            failuresAccessor.DeleteElements(idsToDelete);
            resolvedFailures++;
          }
          else
          {
            if (CommitErrorsDict.ContainsKey(t))
              CommitErrorsDict[t]++;
            else
              CommitErrorsDict.Add(t, 1);
            // currently, the whole commit is rolled back. this should be investigated further at a later date
            // to properly proceed with commit
            failedElements.AddRange(failure.GetFailingElementIds());

            // logging the error
            var speckleEx = new SpeckleException($"Fatal Error: {t}");
            _exceptions.Add(speckleEx);
            SpeckleLog.Logger.Fatal(speckleEx, "Fatal Error: {failureMessage}", t);
          }
        }
      }

      if (resolvedFailures > 0)
        return FailureProcessingResult.ProceedWithCommit;
      else
        return FailureProcessingResult.Continue;
    }

    public SpeckleNonUserFacingException? GetException()
    {
      if (CommitErrorsDict.Count > 0 && _exceptions.Count > 0)
      {
        return new SpeckleNonUserFacingException("Error eater was unable to resolve exceptions", new AggregateException(_exceptions));
      }
      return null;
    }
  }
}
