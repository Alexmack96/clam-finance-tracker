namespace Clam.Api.Tests.Features;

public class GetNotesTests(ClamApiFactory api) : ApiTest(api)
{
    [Fact]
    public async Task Lists_pinned_notes_first()
    {
        await Given.NotesAsync();
        var response = await Get("/api/notes");
        await Verify(response);
    }

    [Fact]
    public async Task Lists_nothing_when_there_are_no_notes()
    {
        await Given.NothingAsync();
        var response = await Get("/api/notes");
        await Verify(response);
    }
}

public class CreateNoteTests(ClamApiFactory api) : ApiTest(api)
{
    [Fact]
    public async Task Creates_a_note()
    {
        await Given.NothingAsync();
        var response = await Post("/api/notes", new { title = "Statements to chase", body = (string?)null });
        await Verify(response);
    }

    [Fact]
    public async Task Rejects_a_note_with_no_title()
    {
        await Given.NothingAsync();
        var response = await Post("/api/notes", new { title = "" });
        await Verify(response);
    }
}

public class UpdateNoteTests(ClamApiFactory api) : ApiTest(api)
{
    [Fact]
    public async Task Leaves_absent_fields_alone()
    {
        await Given.NotesAsync();
        var response = await Patch($"/api/notes/{Arrange.PlainNoteId}", new { pinned = true });
        await Verify(response);
    }

    [Fact]
    public async Task Answers_not_found_for_an_unknown_note()
    {
        await Given.NotesAsync();
        var response = await Patch("/api/notes/nope", new { title = "x" });
        await Verify(response);
    }
}

public class DeleteNoteTests(ClamApiFactory api) : ApiTest(api)
{
    [Fact]
    public async Task Deletes_a_note()
    {
        await Given.NotesAsync();
        var response = await Delete($"/api/notes/{Arrange.PlainNoteId}");
        await Verify(response);
    }

    [Fact]
    public async Task Answers_not_found_for_an_unknown_note()
    {
        await Given.NotesAsync();
        var response = await Delete("/api/notes/nope");
        await Verify(response);
    }
}
