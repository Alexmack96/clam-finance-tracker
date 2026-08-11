namespace Clam.Api.Features.Notes.GetNotes;

/// A bare JSON array, matching `res.json(notes)`.
public sealed class GetNotesResponse : List<NoteRecord>
{
    public GetNotesResponse(IEnumerable<NoteRecord> notes) : base(notes) { }
}
