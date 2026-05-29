import clr

clr.AddReference("RevitServices")
from RevitServices.Persistence import DocumentManager

doc = DocumentManager.Instance.CurrentDBDocument
uiapp = DocumentManager.Instance.CurrentUIApplication
app = uiapp.Application

shared_parameter_path = IN[0] if len(IN) > 0 else None


def normalize_text(value):
    if value is None:
        return ""
    return str(value).strip()


file_path = normalize_text(shared_parameter_path)
original_shared_parameter_path = app.SharedParametersFilename

if not file_path:
    raise Exception("Shared parameter txt path is empty.")

app.SharedParametersFilename = file_path
definition_file = app.OpenSharedParameterFile()

try:
    if definition_file is None:
        raise Exception("Shared parameter txt file could not be opened: {0}".format(file_path))

    groups = []
    for group in definition_file.Groups:
        group_name = normalize_text(group.Name)
        if group_name:
            groups.append(group_name)

    if not groups:
        raise Exception("No shared parameter groups were found in file.")

    indexed_groups = []
    for index, group_name in enumerate(groups):
        indexed_groups.append("{0}: {1}".format(index, group_name))

    OUT = {
        "sharedParameterFile": file_path,
        "groupCount": len(groups),
        "groups": groups,
        "display": indexed_groups,
        "csv": ", ".join(groups)
    }
finally:
    app.SharedParametersFilename = original_shared_parameter_path
