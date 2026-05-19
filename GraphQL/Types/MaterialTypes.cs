namespace AkademVault_API.GraphQL.Types;

// Lecture-material row returned by GraphQL queries.
public record LectureMaterialGql(
    Guid Id,
    string FileName,
    string ContentType,
    long SizeBytes,
    Guid UploaderId,
    string UploaderName,
    DateTime UploadedAt);

// Threaded comment node with nested replies — GraphQL handles the recursion natively.
public record MaterialCommentGql(
    Guid Id,
    Guid? ParentCommentId,
    Guid AuthorId,
    string AuthorName,
    string Content,
    DateTime CreatedAt,
    List<MaterialCommentGql> Replies);

// Full material view with its comment tree, returned by the GraphQL "material" query.
public record MaterialWithCommentsGql(
    Guid Id,
    string FileName,
    string ContentType,
    long SizeBytes,
    Guid GroupId,
    Guid UploaderId,
    string UploaderName,
    DateTime UploadedAt,
    List<MaterialCommentGql> Comments);
