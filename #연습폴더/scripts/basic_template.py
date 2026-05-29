"""
Basic template for a Dynamo Python node.
Copy this into a Python node and edit only the logic you need.
"""

import clr

# Revit / Dynamo references
clr.AddReference("RevitServices")
from RevitServices.Persistence import DocumentManager
from RevitServices.Transactions import TransactionManager

clr.AddReference("RevitAPI")
from Autodesk.Revit.DB import *

clr.AddReference("RevitNodes")
import Revit
clr.ImportExtensions(Revit.Elements)
clr.ImportExtensions(Revit.GeometryConversion)


# Current Revit document
doc = DocumentManager.Instance.CurrentDBDocument
uiapp = DocumentManager.Instance.CurrentUIApplication
app = uiapp.Application
uidoc = uiapp.ActiveUIDocument


# Debug log
debug = []


def to_list(value):
    """Return a list whether the input is single item, tuple, or list."""
    if value is None:
        return []
    if isinstance(value, list):
        return value
    if isinstance(value, tuple):
        return list(value)
    return [value]


def unwrap(value):
    """Unwrap Dynamo elements into Revit API elements."""
    if isinstance(value, list):
        return [UnwrapElement(x) for x in value]
    return UnwrapElement(value)


def mm_to_ft(mm_value):
    """Convert millimeters to Revit internal feet."""
    if mm_value is None:
        return None
    return mm_value / 304.8


# Dynamo inputs
input_0 = IN[0] if len(IN) > 0 else None
input_1 = IN[1] if len(IN) > 1 else None


def main():
    items = to_list(input_0)
    revit_items = unwrap(items)
    offset_ft = mm_to_ft(input_1) if input_1 is not None else None

    debug.append("input_count: {0}".format(len(items)))
    debug.append("offset_ft: {0}".format(offset_ft))

    return {
        "items": revit_items,
        "offset_ft": offset_ft,
        "debug": debug,
    }


# Dynamo output
OUT = main()
